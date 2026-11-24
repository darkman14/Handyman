using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>
/// BUG-0008: Job and Bid use the Postgres xmin system column as optimistic concurrency token, so the loser
/// of two real saves gets DbUpdateConcurrencyException (HTTP 409 job_not_open). Real Postgres, no mocks.
/// </summary>
public class US_0017_ConcurrencyTokenTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Test-only endpoint: two contexts load the same job, both apply the action, both save; the second save must fail.</summary>
    private sealed class RaceEndpointsStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseEndpoints(endpoints =>
                endpoints.MapGet("/test-race/{action}/{jobId:guid}", async (string action, Guid jobId, IServiceScopeFactory scopes) =>
                {
                    using var scopeA = scopes.CreateScope();
                    using var scopeB = scopes.CreateScope();
                    var a = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
                    var b = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
                    var jobA = await a.Jobs.SingleAsync(j => j.Id == jobId);
                    var jobB = await b.Jobs.SingleAsync(j => j.Id == jobId);
                    await Apply(action, a, jobA);
                    await Apply(action, b, jobB);
                    await a.SaveChangesAsync();
                    await b.SaveChangesAsync(); // the loser: throws DbUpdateConcurrencyException -> 409 job_not_open
                    return Results.Ok();
                }));
        };
    }

    private static async Task Apply(string action, AppDbContext db, Job job)
    {
        switch (action)
        {
            case "hire":
                var bids = await db.Bids.Where(x => x.JobId == job.Id).ToListAsync();
                job.Hire(bids.First(x => x.Status == BidStatus.Active), bids);
                break;
            case "finish": job.Finish(Now); break;
            case "cancel": job.Cancel(); break;
        }
    }

    private AppDbContext NewContext() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private async Task<(Guid job, Guid bid)> SeedJob(bool assigned = false)
    {
        await using var db = NewContext();
        var n = Guid.NewGuid().ToString("N");
        var client = new User { Id = Guid.NewGuid(), Phone = "+38160" + Random.Shared.NextInt64(1_000_000, 9_999_999), Role = UserRole.Client, Name = "Ivana" };
        var worker = new User { Id = Guid.NewGuid(), Phone = "+38161" + Random.Shared.NextInt64(1_000_000, 9_999_999), Role = UserRole.Worker, Name = "Jelena" };
        var trade = new Trade { Name = "T" + n };
        var area = new Area { Name = "A" + n, Kind = AreaKind.Neighborhood };
        db.AddRange(client, worker, trade, area);
        await db.SaveChangesAsync();

        var job = new Job { Id = Guid.NewGuid(), ClientId = client.Id, TradeId = trade.Id, AreaId = area.Id, Title = "t", Description = "d", Street = "s", HouseNumber = "1" };
        var bid = new Bid { Id = Guid.NewGuid(), JobId = job.Id, WorkerId = worker.Id, ArrivalWindowStart = Now, ArrivalWindowEnd = Now.AddHours(1), CreatedAt = Now };
        db.AddRange(job, bid);
        await db.SaveChangesAsync();
        if (assigned)
        {
            job.Hire(bid, [bid]);
            await db.SaveChangesAsync();
        }
        return (job.Id, bid.Id);
    }

    [Fact]
    public void Job_and_Bid_have_xmin_as_concurrency_token_and_no_real_column()
    {
        using var db = NewContext();
        foreach (var type in new[] { typeof(Job), typeof(Bid) })
        {
            var p = db.Model.FindEntityType(type)!.FindProperty("Version")!;
            p.IsConcurrencyToken.ShouldBeTrue();
            p.GetColumnName().ShouldBe("xmin");
            p.GetColumnType().ShouldBe("xid");
        }
    }

    [Fact]
    public async Task Version_is_filled_by_the_database_and_changes_on_every_update()
    {
        var (jobId, _) = await SeedJob();
        await using var db = NewContext();
        var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
        var before = job.Version;
        before.ShouldNotBe(0u);

        job.Cancel();
        await db.SaveChangesAsync();
        job.Version.ShouldNotBe(before);
    }

    [Fact]
    public async Task Second_save_of_Hire_on_the_same_job_throws_DbUpdateConcurrencyException()
    {
        var (jobId, _) = await SeedJob();
        await using var a = NewContext();
        await using var b = NewContext();
        var jobA = await a.Jobs.SingleAsync(j => j.Id == jobId);
        var jobB = await b.Jobs.SingleAsync(j => j.Id == jobId);
        await Apply("hire", a, jobA);
        await Apply("hire", b, jobB);

        await a.SaveChangesAsync();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());

        await using var check = NewContext();
        (await check.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId)).Status.ShouldBe(JobStatus.Assigned);
    }

    [Fact]
    public async Task Second_save_of_Finish_on_the_same_job_throws_DbUpdateConcurrencyException()
    {
        var (jobId, _) = await SeedJob(assigned: true);
        await using var a = NewContext();
        await using var b = NewContext();
        var jobA = await a.Jobs.SingleAsync(j => j.Id == jobId);
        var jobB = await b.Jobs.SingleAsync(j => j.Id == jobId);
        jobA.Finish(Now);
        jobB.Finish(Now);

        await a.SaveChangesAsync();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    [Fact]
    public async Task Second_save_of_Cancel_on_the_same_job_throws_DbUpdateConcurrencyException()
    {
        var (jobId, _) = await SeedJob();
        await using var a = NewContext();
        await using var b = NewContext();
        var jobA = await a.Jobs.SingleAsync(j => j.Id == jobId);
        var jobB = await b.Jobs.SingleAsync(j => j.Id == jobId);
        jobA.Cancel();
        jobB.Cancel();

        await a.SaveChangesAsync();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    [Fact]
    public async Task Saves_in_sequence_with_a_fresh_load_do_not_collide()
    {
        var (jobId, _) = await SeedJob(assigned: true);
        await using (var a = NewContext())
        {
            (await a.Jobs.SingleAsync(j => j.Id == jobId)).Finish(Now);
            await a.SaveChangesAsync();
        }
        await using var b = NewContext();
        var job = await b.Jobs.SingleAsync(j => j.Id == jobId);
        job.Status.ShouldBe(JobStatus.Done);
    }

    [Fact]
    public async Task Two_changes_touching_the_same_bid_collide()
    {
        var (_, bidId) = await SeedJob();
        await using var a = NewContext();
        await using var b = NewContext();
        var bidA = await a.Bids.SingleAsync(x => x.Id == bidId);
        var bidB = await b.Bids.SingleAsync(x => x.Id == bidId);
        a.Entry(bidA).Property(x => x.Status).CurrentValue = BidStatus.Withdrawn;
        b.Entry(bidB).Property(x => x.Status).CurrentValue = BidStatus.Rejected;

        await a.SaveChangesAsync();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    [Theory]
    [InlineData("hire", false)]
    [InlineData("finish", true)]
    [InlineData("cancel", false)]
    public async Task Loser_of_a_real_race_gets_409_job_not_open_over_http(string action, bool assigned)
    {
        var (jobId, _) = await SeedJob(assigned);
        var client = factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddTransient<IStartupFilter, RaceEndpointsStartupFilter>())).CreateClient();

        var response = await client.GetAsync($"/test-race/{action}/{jobId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe("job_not_open");
        body.ToString().ShouldNotContain("xmin");

        // the winner's save is the one that stuck
        await using var db = NewContext();
        var status = (await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId)).Status;
        status.ShouldBe(action switch { "hire" => JobStatus.Assigned, "finish" => JobStatus.Done, _ => JobStatus.Cancelled });
    }
}
