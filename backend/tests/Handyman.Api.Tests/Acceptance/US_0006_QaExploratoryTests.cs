using System.Net;
using System.Net.Http.Headers;
using Handyman.Api.Data;
using Handyman.Api.Data.Seed;
using Handyman.Api.Domain;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>QA attack tests for US-0006 (in-process, fake Supabase admin client).</summary>
public class US_0006_QaExploratoryTests
{
    private sealed class FakeAuthAdmin : ISupabaseAdminClient
    {
        private readonly Dictionary<string, Guid> _byPhone = [];
        public Task<Guid> CreateConfirmedPhoneUserAsync(string phone, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            _byPhone[phone] = id;
            return Task.FromResult(id);
        }
        public Task<Guid?> FindUserIdByPhoneAsync(string phone, CancellationToken ct) =>
            Task.FromResult<Guid?>(_byPhone.TryGetValue(phone, out var id) ? id : null);
    }

    private sealed class QaFactory(string cs, FakeAuthAdmin admin, bool seed) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = cs,
                ["Supabase:Url"] = "http://test.local",
                ["Supabase:JwtSecret"] = TestAuth.Secret,
                ["Database:MigrateOnStartup"] = "false",
                ["Seed:Enabled"] = seed ? "true" : "false",
            }));
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<ISupabaseAdminClient>();
                s.AddSingleton<ISupabaseAdminClient>(admin);
            });
        }
    }

    private const string Ana = "+381600000201", Milan = "+381600000202", Ivana = "+381600000203";
    private const string Jelena = "+381600000102";

    private static async Task SeedAsync(TestDatabase db, FakeAuthAdmin admin)
    {
        await using var factory = new QaFactory(db.GetConnectionString(), admin, true);
        _ = factory.Services;
    }

    private static async Task RunStepsAsync(TestDatabase db, FakeAuthAdmin admin, params Type[] steps)
    {
        await using var factory = new QaFactory(db.GetConnectionString(), admin, false);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        foreach (var t in steps)
            await scope.ServiceProvider.GetServices<IDevSeedStep>().Single(s => s.GetType() == t).SeedAsync(default);
    }

    private static async Task<List<T>> QueryAsync<T>(TestDatabase db, string sql, Func<NpgsqlDataReader, T> map)
    {
        await using var conn = new NpgsqlConnection(db.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await r.ReadAsync()) rows.Add(map(r));
        return rows;
    }

    private static async Task<long> CountAsync(TestDatabase db, string sql) => (await QueryAsync(db, sql, r => r.GetInt64(0))).Single();

    private static async Task ExecAsync(TestDatabase db, string sql)
    {
        await using var conn = new NpgsqlConnection(db.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task<List<(string Phone, int Number)>> NumbersAsync(TestDatabase db) => QueryAsync(db,
        "SELECT u.phone, j.number FROM app.jobs j JOIN app.users u ON u.id = j.client_id ORDER BY j.number", r => (r.GetString(0), r.GetInt32(1)));

    [Fact]
    public async Task Bidders_cover_the_job_neighborhood_and_trade()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());
            var uncovered = await CountAsync(db,
                "SELECT count(*) FROM app.bids b JOIN app.jobs j ON j.id = b.job_id WHERE " +
                "NOT EXISTS (SELECT 1 FROM app.worker_areas wa WHERE wa.worker_id = b.worker_id AND wa.area_id = j.area_id) OR " +
                "NOT EXISTS (SELECT 1 FROM app.worker_trades wt WHERE wt.worker_id = b.worker_id AND wt.trade_id = j.trade_id)");
            Assert.Equal(0, uncovered);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Bids_have_positive_money_valid_utc_windows_and_future_windows_on_open_jobs()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());
            Assert.Equal(0, await CountAsync(db,
                "SELECT count(*) FROM app.bids WHERE diagnostic_fee <= 0 OR hourly_rate <= 0 OR arrival_window_end <= arrival_window_start OR created_at > arrival_window_start"));
            Assert.Equal(0, await CountAsync(db,
                "SELECT count(*) FROM app.bids b JOIN app.jobs j ON j.id = b.job_id WHERE j.status = 'Open' AND b.arrival_window_start < now()"));
            Assert.Equal(1, await CountAsync(db, "SELECT count(*) FROM app.bids WHERE status = 'Accepted'"));
            Assert.Equal(2, await CountAsync(db, "SELECT count(*) FROM app.bids WHERE status = 'Active'"));
            Assert.Equal(3, await CountAsync(db, "SELECT count(*) FROM app.bids"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.bids b JOIN app.jobs j ON j.id = b.job_id WHERE j.status = 'Done' AND b.id <> j.accepted_bid_id"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Job_numbers_increase_in_order_ana_milan_ivana_across_repeated_reseeds_and_a_dirty_sequence()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            Assert.Equal([Ana, Milan, Ivana], (await NumbersAsync(db)).Select(x => x.Phone).ToArray());

            var last = (await NumbersAsync(db)).Max(x => x.Number);
            for (var i = 0; i < 5; i++)
            {
                await ExecAsync(db, "DELETE FROM app.bids; DELETE FROM app.jobs");
                if (i == 2) await ExecAsync(db, "ALTER TABLE app.jobs ALTER COLUMN number RESTART WITH 1000");
                await SeedAsync(db, admin);
                var rows = await NumbersAsync(db);
                Assert.Equal([Ana, Milan, Ivana], rows.Select(x => x.Phone).ToArray());
                Assert.Equal(3, rows.Select(x => x.Number).Distinct().Count());
                Assert.True(rows[0].Number > last || i == 2);
                last = rows[2].Number;
            }
            Assert.True(last >= 1000);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Seeded_done_job_obeys_the_domain_model_and_can_be_rated_and_closed()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());
            await using var factory = new QaFactory(db.GetConnectionString(), new FakeAuthAdmin(), false);
            using var scope = factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var done = await ctx.Jobs.SingleAsync(j => j.Status == JobStatus.Done);
            var bids = await ctx.Bids.Where(b => b.JobId == done.Id).ToListAsync();
            var accepted = Assert.Single(bids);
            Assert.Equal(done.AcceptedBidId, accepted.Id);
            Assert.Equal(BidStatus.Accepted, accepted.Status);
            Assert.NotNull(done.FinishedAt);
            Assert.Null(done.ClosedAt);

            Assert.Equal("job_not_open", Assert.Throws<DomainException>(() => done.Hire(accepted, bids)).Code);
            Assert.Equal("job_not_assigned", Assert.Throws<DomainException>(() => done.Finish(DateTimeOffset.UtcNow)).Code);
            Assert.Equal("job_not_cancellable", Assert.Throws<DomainException>(() => done.Cancel()).Code);

            var clientRating = done.AddRating(done.ClientId, 5, null, DateTimeOffset.UtcNow, accepted, []);
            Assert.Equal(JobStatus.Done, done.Status);
            ctx.Ratings.Add(clientRating);
            await ctx.SaveChangesAsync();
            var workerRating = done.AddRating(accepted.WorkerId, 4, null, DateTimeOffset.UtcNow, accepted, [clientRating]);
            ctx.Ratings.Add(workerRating);
            await ctx.SaveChangesAsync();
            Assert.Equal(JobStatus.Closed, done.Status);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Open_job_with_two_bids_can_be_hired_through_the_domain_and_the_other_bid_is_rejected()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());
            await using var factory = new QaFactory(db.GetConnectionString(), new FakeAuthAdmin(), false);
            using var scope = factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jobId = (await QueryAsync(db, "SELECT j.id FROM app.jobs j JOIN app.bids b ON b.job_id = j.id WHERE j.status = 'Open' GROUP BY j.id", r => r.GetGuid(0))).Single();
            var job = await ctx.Jobs.SingleAsync(j => j.Id == jobId);
            var bids = await ctx.Bids.Where(b => b.JobId == job.Id).ToListAsync();
            Assert.Equal(2, bids.Count);
            job.Hire(bids[0], bids);
            await ctx.SaveChangesAsync();
            Assert.Equal(1, await CountAsync(db, "SELECT count(*) FROM app.bids WHERE status = 'Rejected'"));
            Assert.Equal(JobStatus.Assigned, job.Status);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Database_refuses_a_second_bid_by_the_same_worker_on_a_seeded_job()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());
            await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(db,
                "INSERT INTO app.bids (id, job_id, worker_id, arrival_window_start, arrival_window_end, diagnostic_fee, hourly_rate, status, created_at) " +
                "SELECT gen_random_uuid(), job_id, worker_id, arrival_window_start, arrival_window_end, 1, 1, 'Active', now() FROM app.bids LIMIT 1"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Seed_is_all_or_nothing_when_a_bid_insert_fails_midway()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await RunStepsAsync(db, admin, typeof(SeedDevUsersStep), typeof(SeedDevWorkersStep));
            await ExecAsync(db, "ALTER TABLE app.bids ADD CONSTRAINT qa_break CHECK (diagnostic_fee < 0)");
            await Assert.ThrowsAnyAsync<Exception>(() => RunStepsAsync(db, admin, typeof(SeedDevSampleJobsStep)));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.jobs"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.bids"));

            await ExecAsync(db, "ALTER TABLE app.bids DROP CONSTRAINT qa_break");
            await RunStepsAsync(db, admin, typeof(SeedDevSampleJobsStep));
            Assert.Equal(3, await CountAsync(db, "SELECT count(*) FROM app.jobs"));
            Assert.Equal(3, await CountAsync(db, "SELECT count(*) FROM app.bids"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Missing_a_single_user_or_neighborhood_fails_loudly_and_writes_nothing()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await RunStepsAsync(db, admin, typeof(SeedDevUsersStep), typeof(SeedDevWorkersStep));
            await ExecAsync(db, "UPDATE app.users SET phone = '+000' WHERE phone = '" + Ivana + "'");
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunStepsAsync(db, admin, typeof(SeedDevSampleJobsStep)));
            Assert.Contains(Ivana, ex.Message);
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.jobs"));

            await ExecAsync(db, "UPDATE app.users SET phone = '" + Ivana + "' WHERE phone = '+000'");
            await ExecAsync(db, "UPDATE app.areas SET name = 'Deram-renamed' WHERE name = 'Đeram'");
            var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => RunStepsAsync(db, admin, typeof(SeedDevSampleJobsStep)));
            Assert.Contains("Đeram", ex2.Message);
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.jobs"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Restarting_three_times_after_tester_edits_changes_nothing()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            await ExecAsync(db, "UPDATE app.jobs SET title = 'edited by tester' WHERE title LIKE 'Socket%'");
            await ExecAsync(db, "UPDATE app.bids SET status = 'Withdrawn' WHERE hourly_rate = 2200");
            const string jobSql = "SELECT id, title, status, number FROM app.jobs ORDER BY number";
            const string bidSql = "SELECT id, status, hourly_rate FROM app.bids ORDER BY id";
            var jobs = await QueryAsync(db, jobSql, r => $"{r.GetGuid(0)}|{r.GetString(1)}|{r.GetString(2)}|{r.GetInt32(3)}");
            var bids = await QueryAsync(db, bidSql, r => $"{r.GetGuid(0)}|{r.GetString(1)}|{r.GetInt32(2)}");
            for (var i = 0; i < 3; i++) await SeedAsync(db, admin);
            Assert.Equal(jobs, await QueryAsync(db, jobSql, r => $"{r.GetGuid(0)}|{r.GetString(1)}|{r.GetString(2)}|{r.GetInt32(3)}"));
            Assert.Equal(bids, await QueryAsync(db, bidSql, r => $"{r.GetGuid(0)}|{r.GetString(1)}|{r.GetInt32(2)}"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task KNOWN_GAP_two_hosts_seeding_at_once_may_duplicate_the_sample_set_but_never_partially()
    {
        // BUG-0018: "no jobs exist" check and insert are not serialised. This test pins what always holds
        // (whole sets only, distinct numbers) and tolerates the duplicate.
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await RunStepsAsync(db, admin, typeof(SeedDevUsersStep), typeof(SeedDevWorkersStep));
            var runs = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                try { await RunStepsAsync(db, admin, typeof(SeedDevSampleJobsStep)); } catch (Exception) { }
            })).ToArray();
            await Task.WhenAll(runs);
            var jobs = await CountAsync(db, "SELECT count(*) FROM app.jobs");
            Assert.True(jobs >= 3 && jobs % 3 == 0);
            Assert.Equal(jobs, await CountAsync(db, "SELECT count(DISTINCT number) FROM app.jobs"));
            Assert.Equal(jobs, await CountAsync(db, "SELECT count(*) FROM app.bids") / 3 * 3);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Owners_can_call_me_and_public_endpoints_do_not_expose_the_sample_jobs()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            await using var factory = new QaFactory(db.GetConnectionString(), admin, false);
            var ids = await QueryAsync(db, "SELECT id FROM app.users WHERE phone IN ('" + Ana + "','" + Milan + "','" + Ivana + "','" + Jelena + "')", r => r.GetGuid(0));
            Assert.Equal(4, ids.Count);
            using var client = factory.CreateClient();
            foreach (var id in ids)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.TokenFor(id));
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
            }
            client.DefaultRequestHeaders.Authorization = null;
            foreach (var path in new[] { "/api/jobs", "/api/jobs/open", "/api/bids/mine", "/api/trades", "/api/areas" })
            {
                var resp = await client.GetAsync(path);
                var body = await resp.Content.ReadAsStringAsync();
                Assert.DoesNotContain("ishwasher", body);
                Assert.DoesNotContain("Njegoševa", body);
                Assert.DoesNotContain("gate code", body);
            }
        }
        finally { await db.DisposeAsync(); }
    }
}
