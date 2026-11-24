using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>
/// QA (qa-api) independent checks of the xmin concurrency token (BUG-0008): no false conflicts, rating race,
/// ReadDbContext reads, raw SQL behaviour. Real Postgres.
/// </summary>
public class US_0017_QaConcurrencyTokenTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    private AppDbContext NewContext() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
    private ReadDbContext NewReadContext() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<ReadDbContext>();

    private record Seed(Guid JobId, Guid ClientId, Guid WorkerId, Guid[] BidIds);

    private async Task<Seed> SeedJob(int bids = 1, bool done = false)
    {
        await using var db = NewContext();
        var n = Guid.NewGuid().ToString("N");
        var client = new User { Id = Guid.NewGuid(), Phone = "+38162" + Random.Shared.NextInt64(1_000_000, 9_999_999), Role = UserRole.Client, Name = "Ivana" };
        var trade = new Trade { Name = "T" + n };
        var area = new Area { Name = "A" + n, Kind = AreaKind.Neighborhood };
        var workers = Enumerable.Range(0, bids).Select(_ => new User { Id = Guid.NewGuid(), Phone = "+38163" + Random.Shared.NextInt64(1_000_000, 9_999_999), Role = UserRole.Worker, Name = "W" }).ToList();
        db.AddRange(client, trade, area);
        db.AddRange(workers);
        await db.SaveChangesAsync();

        var job = new Job { Id = Guid.NewGuid(), ClientId = client.Id, TradeId = trade.Id, AreaId = area.Id, Title = "t", Description = "d", Street = "s", HouseNumber = "1" };
        db.Add(job);
        var bidList = workers.Select(w => new Bid { Id = Guid.NewGuid(), JobId = job.Id, WorkerId = w.Id, ArrivalWindowStart = Now, ArrivalWindowEnd = Now.AddHours(1), CreatedAt = Now }).ToList();
        db.AddRange(bidList);
        await db.SaveChangesAsync();
        if (done)
        {
            job.Hire(bidList[0], bidList);
            await db.SaveChangesAsync();
            job.Finish(Now);
            await db.SaveChangesAsync();
        }
        return new Seed(job.Id, client.Id, workers[0].Id, bidList.Select(b => b.Id).ToArray());
    }

    [Fact]
    public async Task Saving_unchanged_entities_and_saving_twice_in_one_context_never_conflicts()
    {
        var s = await SeedJob(2);
        await using var db = NewContext();
        var job = await db.Jobs.SingleAsync(j => j.Id == s.JobId);
        var bids = await db.Bids.Where(b => b.JobId == s.JobId).ToListAsync();
        (await db.SaveChangesAsync()).ShouldBe(0);

        var bids2 = bids.OrderBy(b => b.Id).ToList();
        job.Hire(bids2[0], bids2);
        await db.SaveChangesAsync();   // job + 2 bids in one SaveChanges
        job.Finish(Now);
        await db.SaveChangesAsync();   // same context again, token refreshed from RETURNING
        job.Status.ShouldBe(JobStatus.Done);

        await using var check = NewContext();
        var stored = await check.Bids.AsNoTracking().Where(b => b.JobId == s.JobId).ToListAsync();
        stored.Count(b => b.Status == BidStatus.Accepted).ShouldBe(1);
        stored.Count(b => b.Status == BidStatus.Rejected).ShouldBe(1);
    }

    [Fact]
    public async Task Save_after_reload_and_two_different_jobs_in_parallel_do_not_conflict()
    {
        var s1 = await SeedJob();
        var s2 = await SeedJob();
        await using var a = NewContext();
        await using var b = NewContext();
        var j1 = await a.Jobs.SingleAsync(j => j.Id == s1.JobId);
        var j2 = await b.Jobs.SingleAsync(j => j.Id == s2.JobId);
        j1.Cancel();
        j2.Cancel();
        await Task.WhenAll(a.SaveChangesAsync(), b.SaveChangesAsync());

        // stale context, reload, change, save
        await using var c = NewContext();
        var stale = await c.Jobs.SingleAsync(j => j.Id == s1.JobId);
        await using (var other = NewContext())
        {
            // another writer touches an unrelated column of the same row
            await other.Database.ExecuteSqlRawAsync("update app.jobs set title = 'x' where id = {0}", s1.JobId);
        }
        await c.Entry(stale).ReloadAsync();
        stale.Status.ShouldBe(JobStatus.Cancelled);
        c.Entry(stale).Property(x => x.Title).CurrentValue = "y";
        await c.SaveChangesAsync();
    }

    [Fact]
    public async Task Hire_loses_against_a_concurrent_bid_withdrawal_even_though_the_job_row_was_not_touched()
    {
        var s = await SeedJob(2);
        await using var a = NewContext(); // withdraws bid 0
        await using var b = NewContext(); // hires bid 0
        var bidA = await a.Bids.SingleAsync(x => x.Id == s.BidIds[0]);
        var jobB = await b.Jobs.SingleAsync(j => j.Id == s.JobId);
        var bidsB = await b.Bids.Where(x => x.JobId == s.JobId).ToListAsync();
        a.Entry(bidA).Property(x => x.Status).CurrentValue = BidStatus.Withdrawn;
        jobB.Hire(bidsB.Single(x => x.Id == s.BidIds[0]), bidsB);

        await a.SaveChangesAsync();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());

        await using var check = NewContext();
        (await check.Jobs.AsNoTracking().SingleAsync(j => j.Id == s.JobId)).Status.ShouldBe(JobStatus.Open);
    }

    [Fact]
    public async Task ReadDbContext_reads_jobs_and_bids_with_and_without_projection()
    {
        var s = await SeedJob();
        await using var read = NewReadContext();
        (await read.Jobs.SingleAsync(j => j.Id == s.JobId)).Version.ShouldNotBe(0u);
        (await read.Bids.SingleAsync(b => b.Id == s.BidIds[0])).Version.ShouldNotBe(0u);
        (await read.Jobs.Where(j => j.Id == s.JobId).Select(j => j.Title).SingleAsync()).ShouldBe("t");
        (await read.Jobs.Join(read.Bids, j => j.Id, b => b.JobId, (j, b) => new { j.Id, b.Status })
            .Where(x => x.Id == s.JobId).ToListAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Raw_sql_select_star_on_jobs_works_only_when_xmin_is_selected_too()
    {
        // Documents the gotcha for future handlers: FromSql must return every mapped column, xmin included.
        var s = await SeedJob();
        await using var db = NewContext();
        await Should.ThrowAsync<Exception>(() =>
            db.Jobs.FromSqlRaw("select * from app.jobs where id = {0}", s.JobId).ToListAsync());

        await using var db2 = NewContext();
        var ok = await db2.Jobs.FromSqlRaw("select *, xmin from app.jobs where id = {0}", s.JobId).ToListAsync();
        ok.Single().Version.ShouldNotBe(0u);
    }

    [Fact]
    public async Task Jobs_and_bids_tables_have_no_extra_version_or_xmin_user_column()
    {
        await using var db = NewContext();
        var cols = await db.Database.SqlQueryRaw<string>(
            "select column_name as \"Value\" from information_schema.columns where table_schema = 'app' and table_name in ('jobs','bids') and column_name in ('xmin','version')").ToListAsync();
        cols.ShouldBeEmpty();
    }

    /// <summary>
    /// Both parties rate at the same moment on a Done job. Each handler sees no counterpart rating, so neither closes the job and
    /// the job row is not updated, so the xmin token does not detect the race. The job stays Done with both ratings stored.
    /// KNOWN_GAP_BUG_0010: flip to expect a DbUpdateConcurrencyException (or a Closed job) when fixed.
    /// </summary>
    [Fact]
    public async Task KNOWN_GAP_BUG_0010_parallel_ratings_by_both_parties_leave_the_job_Done_not_Closed()
    {
        var s = await SeedJob(done: true);
        await using var a = NewContext();
        await using var b = NewContext();
        var jobA = await a.Jobs.SingleAsync(j => j.Id == s.JobId);
        var jobB = await b.Jobs.SingleAsync(j => j.Id == s.JobId);
        var bidA = await a.Bids.SingleAsync(x => x.Id == s.BidIds[0]);
        var bidB = await b.Bids.SingleAsync(x => x.Id == s.BidIds[0]);
        var ratingsA = await a.Ratings.Where(r => r.JobId == s.JobId).ToListAsync();
        var ratingsB = await b.Ratings.Where(r => r.JobId == s.JobId).ToListAsync();

        a.Add(jobA.AddRating(s.ClientId, 5, null, Now, bidA, ratingsA));
        b.Add(jobB.AddRating(s.WorkerId, 4, null, Now, bidB, ratingsB));
        await a.SaveChangesAsync();
        await b.SaveChangesAsync(); // no exception today

        await using var check = NewContext();
        (await check.Ratings.CountAsync(r => r.JobId == s.JobId)).ShouldBe(2);
        (await check.Jobs.AsNoTracking().SingleAsync(j => j.Id == s.JobId)).Status.ShouldBe(JobStatus.Done); // should be Closed
    }
}
