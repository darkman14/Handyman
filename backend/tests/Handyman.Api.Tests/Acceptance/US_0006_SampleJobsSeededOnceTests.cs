using Handyman.Api.Data.Seed;
using Handyman.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Handyman.Api.Tests.Acceptance;

public class US_0006_SampleJobsSeededOnceTests
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

    private sealed class SeedingFactory(string cs, FakeAuthAdmin admin, bool seed) : WebApplicationFactory<Program>
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
    private const string Jelena = "+381600000102", Nikola = "+381600000103";

    private static async Task SeedAsync(TestDatabase db, FakeAuthAdmin admin)
    {
        await using var factory = new SeedingFactory(db.GetConnectionString(), admin, true);
        _ = factory.Services;
    }

    private static async Task RunStepsAsync(TestDatabase db, FakeAuthAdmin admin, params Type[] steps)
    {
        await using var factory = new SeedingFactory(db.GetConnectionString(), admin, false);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Handyman.Api.Data.AppDbContext>().Database.MigrateAsync();
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

    private const string JobSelect =
        "SELECT j.id, j.number, j.status, j.title, j.description, a.name, t.name, u.phone, j.street, j.house_number, j.entrance, j.floor, j.apartment, j.address_note, j.accepted_bid_id, j.created_at, j.finished_at " +
        "FROM app.jobs j JOIN app.areas a ON a.id = j.area_id JOIN app.trades t ON t.id = j.trade_id JOIN app.users u ON u.id = j.client_id";

    private sealed record JobRow(Guid Id, int Number, string Status, string Title, string Description, string Area, string Trade, string ClientPhone,
        string Street, string House, string? Entrance, string? Floor, string? Apartment, string? Note, Guid? AcceptedBidId, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);

    private static Task<List<JobRow>> JobsAsync(TestDatabase db) => QueryAsync(db, JobSelect + " ORDER BY j.number", r => new JobRow(
        r.GetGuid(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
        r.GetString(8), r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11),
        r.IsDBNull(12) ? null : r.GetString(12), r.IsDBNull(13) ? null : r.GetString(13), r.IsDBNull(14) ? null : r.GetGuid(14),
        r.GetFieldValue<DateTimeOffset>(15), r.IsDBNull(16) ? null : r.GetFieldValue<DateTimeOffset>(16)));

    private sealed record BidRow(Guid Id, Guid JobId, string WorkerPhone, string Status, DateTimeOffset Start, DateTimeOffset End, int Fee, int Rate);

    private static Task<List<BidRow>> BidsAsync(TestDatabase db) => QueryAsync(db,
        "SELECT b.id, b.job_id, u.phone, b.status, b.arrival_window_start, b.arrival_window_end, b.diagnostic_fee, b.hourly_rate FROM app.bids b JOIN app.users u ON u.id = b.worker_id ORDER BY u.phone",
        r => new BidRow(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4), r.GetFieldValue<DateTimeOffset>(5), r.GetInt32(6), r.GetInt32(7)));

    [Fact]
    public async Task AC1_ana_has_open_dishwasher_job_with_two_active_bids()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());

            var job = Assert.Single((await JobsAsync(db)).Where(j => j.ClientPhone == Ana));
            Assert.Equal("Open", job.Status);
            Assert.Equal("Appliance repair", job.Trade);
            Assert.Equal("Čubura", job.Area);
            Assert.Contains("ishwasher", job.Title);
            Assert.False(string.IsNullOrWhiteSpace(job.Description));
            Assert.Null(job.AcceptedBidId);

            var bids = (await BidsAsync(db)).Where(b => b.JobId == job.Id).ToList();
            Assert.Equal(2, bids.Count);
            Assert.Equal([Jelena, Nikola], bids.Select(b => b.WorkerPhone).ToArray());
            foreach (var b in bids)
            {
                Assert.Equal("Active", b.Status);
                Assert.True(b.End > b.Start);
                Assert.Equal(TimeSpan.Zero, b.Start.Offset);
                Assert.Equal(TimeSpan.Zero, b.End.Offset);
                Assert.True(b.Fee > 0);
                Assert.True(b.Rate > 0);
            }
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task AC2_milan_has_open_electrical_job_without_bids()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());

            var job = Assert.Single((await JobsAsync(db)).Where(j => j.ClientPhone == Milan));
            Assert.Equal("Open", job.Status);
            Assert.Equal("Electrical", job.Trade);
            Assert.Equal("Bežanijska kosa", job.Area);
            Assert.False(string.IsNullOrWhiteSpace(job.Title));
            Assert.False(string.IsNullOrWhiteSpace(job.Description));
            Assert.Empty((await BidsAsync(db)).Where(b => b.JobId == job.Id));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task AC3_ivana_has_done_job_hired_to_jelena_without_ratings()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());

            var job = Assert.Single((await JobsAsync(db)).Where(j => j.ClientPhone == Ivana));
            Assert.Equal("Done", job.Status);
            Assert.Equal("Appliance repair", job.Trade);
            Assert.Equal("Đeram", job.Area);
            Assert.NotNull(job.FinishedAt);
            Assert.True(job.FinishedAt > job.CreatedAt);
            Assert.Equal(TimeSpan.Zero, job.CreatedAt.Offset);
            Assert.Equal(TimeSpan.Zero, job.FinishedAt!.Value.Offset);

            var bid = Assert.Single((await BidsAsync(db)).Where(b => b.JobId == job.Id));
            Assert.Equal(Jelena, bid.WorkerPhone);
            Assert.Equal("Accepted", bid.Status);
            Assert.Equal(bid.Id, job.AcceptedBidId);
            Assert.True(bid.End > bid.Start);
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.ratings"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.jobs WHERE status = 'Closed'"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task AC4_every_job_has_all_address_fields_and_numbers_increase()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());

            var jobs = await JobsAsync(db);
            Assert.Equal(3, jobs.Count);
            foreach (var j in jobs)
            {
                Assert.False(string.IsNullOrWhiteSpace(j.Street));
                Assert.False(string.IsNullOrWhiteSpace(j.House));
                Assert.False(string.IsNullOrWhiteSpace(j.Entrance));
                Assert.False(string.IsNullOrWhiteSpace(j.Floor));
                Assert.False(string.IsNullOrWhiteSpace(j.Apartment));
                Assert.False(string.IsNullOrWhiteSpace(j.Note));
            }
            Assert.Equal(3, jobs.Select(j => j.Number).Distinct().Count());
            Assert.Equal([Ana, Milan, Ivana], jobs.Select(j => j.ClientPhone).ToArray());
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task AC5_restart_with_jobs_present_changes_nothing_and_deleting_all_brings_three_back()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            var before = await JobsAsync(db);
            var bidsBefore = await BidsAsync(db);

            await SeedAsync(db, admin); // restart: nothing changes
            Assert.Equal(before, await JobsAsync(db));
            Assert.Equal(bidsBefore, await BidsAsync(db));

            // A tester deletes one sample job (and its bids): restart brings nothing back.
            var milanJob = before.Single(j => j.ClientPhone == Milan);
            await ExecAsync(db, $"DELETE FROM app.jobs WHERE id = '{milanJob.Id}'");
            await SeedAsync(db, admin);
            Assert.Equal(2, await CountAsync(db, "SELECT count(*) FROM app.jobs"));
            Assert.Equal(0, await CountAsync(db, $"SELECT count(*) FROM app.jobs WHERE id = '{milanJob.Id}'"));
            Assert.Equal(bidsBefore.Count, (await BidsAsync(db)).Count);

            // Everything gone: the three come back.
            await ExecAsync(db, "DELETE FROM app.bids; DELETE FROM app.jobs");
            await SeedAsync(db, admin);
            Assert.Equal(3, await CountAsync(db, "SELECT count(*) FROM app.jobs"));
            Assert.Equal(3, await CountAsync(db, "SELECT count(*) FROM app.bids"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Missing_users_make_the_step_fail_clearly_and_write_nothing()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RunStepsAsync(db, new FakeAuthAdmin(), typeof(SeedDevSampleJobsStep)));
            Assert.Contains("seed steps must run first", ex.Message);
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.jobs"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.bids"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task A_tester_created_job_alone_stops_the_sample_jobs()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await RunStepsAsync(db, admin, typeof(SeedDevUsersStep), typeof(SeedDevWorkersStep));
            await ExecAsync(db,
                "INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, created_at) " +
                $"SELECT gen_random_uuid(), u.id, 1, 't', 'd', u.home_area_id, 's', '1', 'Open', now() FROM app.users u WHERE u.phone = '{Ana}'");
            await RunStepsAsync(db, admin, typeof(SeedDevSampleJobsStep));
            Assert.Equal(1, await CountAsync(db, "SELECT count(*) FROM app.jobs"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.bids"));
        }
        finally { await db.DisposeAsync(); }
    }
}
