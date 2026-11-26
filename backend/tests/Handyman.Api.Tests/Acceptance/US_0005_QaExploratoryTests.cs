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

/// <summary>QA attack tests for US-0005 (in-process, fake Supabase admin client).</summary>
public class US_0005_QaExploratoryTests
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

    private const string Marko = "+381600000101", Jelena = "+381600000102", Nikola = "+381600000103", Stefan = "+381600000104";

    private static async Task SeedAsync(TestDatabase db, FakeAuthAdmin admin)
    {
        await using var factory = new QaFactory(db.GetConnectionString(), admin, true);
        _ = factory.Services;
    }

    /// <summary>Migrates, then runs only the chosen seed steps by hand (host started without seeding).</summary>
    private static async Task RunStepsAsync(TestDatabase db, FakeAuthAdmin admin, params Type[] stepTypes)
    {
        await using var factory = new QaFactory(db.GetConnectionString(), admin, false);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Handyman.Api.Data.AppDbContext>().Database.MigrateAsync();
        foreach (var t in stepTypes)
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

    private static async Task<List<string>> TradesAsync(TestDatabase db, string phone) =>
        await QueryAsync(db, $"SELECT t.name FROM app.worker_trades wt JOIN app.users u ON u.id = wt.worker_id JOIN app.trades t ON t.id = wt.trade_id WHERE u.phone = '{phone}' ORDER BY t.name", r => r.GetString(0));

    private static async Task<List<int>> CoverageIdsAsync(TestDatabase db, string phone) =>
        await QueryAsync(db, $"SELECT wa.area_id FROM app.worker_areas wa JOIN app.users u ON u.id = wa.worker_id WHERE u.phone = '{phone}' ORDER BY wa.area_id", r => r.GetInt32(0));

    private static async Task<string> SnapshotAsync(TestDatabase db)
    {
        var p = await QueryAsync(db, "SELECT user_id::text || '|' || coalesce(approved_at::text,'null') || '|' || coalesce(bio,'') FROM app.worker_profiles ORDER BY user_id", r => r.GetString(0));
        var t = await QueryAsync(db, "SELECT worker_id::text || '|' || trade_id FROM app.worker_trades ORDER BY 1", r => r.GetString(0));
        var a = await QueryAsync(db, "SELECT worker_id::text || '|' || area_id FROM app.worker_areas ORDER BY 1", r => r.GetString(0));
        return string.Join("\n", p) + "\n--\n" + string.Join("\n", t) + "\n--\n" + string.Join("\n", a);
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException) parts.Add(e.Message);
        return string.Join(" | ", parts);
    }

    [Fact]
    public async Task Exact_data_per_worker_and_coverage_count_matches_reference_data()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());

            var expected = new Dictionary<string, (string[] Trades, string[] Munis)>
            {
                [Marko] = (["Electrical", "Heating & AC"], ["Novi Beograd", "Zvezdara"]),
                [Jelena] = (["Appliance repair"], ["Vračar", "Zvezdara"]),
                [Nikola] = (["Appliance repair", "Plumbing"], ["Vračar", "Novi Beograd"]),
                [Stefan] = (["Carpentry", "Painting"], ["Vračar"]),
            };
            foreach (var (phone, (trades, munis)) in expected)
            {
                Assert.Equal(trades.Order().ToList(), await TradesAsync(db, phone));
                var perMuni = 0L;
                foreach (var m in munis)
                    perMuni += await CountAsync(db, $"SELECT count(*) FROM app.areas n JOIN app.areas m ON m.id=n.parent_id WHERE m.name='{m}' AND m.kind='Municipality' AND n.kind='Neighborhood'");
                var cover = await CoverageIdsAsync(db, phone);
                Assert.Equal(perMuni, cover.Count);
                Assert.Equal(cover.Count, cover.Distinct().Count());
            }
            // totals: 7 trade links, no coverage by anyone but the four workers
            Assert.Equal(7, await CountAsync(db, "SELECT count(*) FROM app.worker_trades"));
            Assert.Equal(4, await CountAsync(db, "SELECT count(DISTINCT worker_id) FROM app.worker_areas"));
            Assert.Equal(4, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles"));
            // clients and admin: nothing
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles p JOIN app.users u ON u.id=p.user_id WHERE u.role<>'Worker'"));
            // all seeded workers have role Worker
            Assert.Equal(4, await CountAsync(db, "SELECT count(*) FROM app.users WHERE role='Worker'"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Approved_at_is_set_now_not_in_the_future_and_stefan_is_null()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var before = DateTimeOffset.UtcNow.AddSeconds(-5);
            await SeedAsync(db, new FakeAuthAdmin());
            var after = DateTimeOffset.UtcNow.AddSeconds(5);

            var rows = await QueryAsync(db, "SELECT u.phone, p.approved_at FROM app.worker_profiles p JOIN app.users u ON u.id=p.user_id",
                r => (r.GetString(0), r.IsDBNull(1) ? (DateTimeOffset?)null : r.GetFieldValue<DateTimeOffset>(1)));
            foreach (var (phone, at) in rows)
            {
                if (phone == Stefan) { Assert.Null(at); continue; }
                Assert.NotNull(at);
                Assert.InRange(at!.Value, before, after);
            }
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Three_restarts_change_nothing_at_all()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            var first = await SnapshotAsync(db);
            for (var i = 0; i < 3; i++) await SeedAsync(db, admin);
            Assert.Equal(first, await SnapshotAsync(db));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Workers_step_without_users_step_fails_clearly_and_writes_nothing()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RunStepsAsync(db, new FakeAuthAdmin(), typeof(SeedDevWorkersStep)));
            Assert.Contains("users seed step must run first", ex.Message);
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_trades"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_areas"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Missing_user_for_one_worker_fails_and_leaves_no_half_worker()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await RunStepsAsync(db, admin, typeof(SeedDevUsersStep));
            await ExecAsync(db, $"DELETE FROM app.users WHERE phone = '{Stefan}'"); // the last worker in the list
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunStepsAsync(db, admin, typeof(SeedDevWorkersStep)));
            Assert.Contains(Stefan, ex.Message);
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_trades"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_areas"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("DELETE FROM app.trades WHERE name = 'Carpentry'", "Carpentry")]
    [InlineData("UPDATE app.areas SET name = 'Zvezdara X' WHERE name = 'Zvezdara' AND kind = 'Municipality'", "Zvezdara")]
    public async Task Missing_reference_data_fails_loudly_and_all_or_nothing(string breakSql, string expectedInMessage)
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await RunStepsAsync(db, admin, typeof(SeedDevUsersStep));
            await ExecAsync(db, breakSql);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunStepsAsync(db, admin, typeof(SeedDevWorkersStep)));
            Assert.Contains(expectedInMessage, ex.Message);
            Assert.Contains("missing from the reference data", ex.Message);
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_trades"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_areas"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Failing_host_start_with_missing_trade_stops_the_host()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            // wipe workers, then break reference data and "restart"
            await ExecAsync(db, "DELETE FROM app.worker_areas; DELETE FROM app.worker_trades; DELETE FROM app.worker_profiles; DELETE FROM app.trades WHERE name = 'Painting'");
            var ex = Assert.ThrowsAny<Exception>(() => new QaFactory(db.GetConnectionString(), admin, true).Services);
            Assert.Contains("Painting", Flatten(ex));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles"));
        }
        finally { await db.DisposeAsync(); }
    }

    /// <summary>
    /// Documented behaviour (by design, "a worker with an existing profile is skipped entirely"): a worker whose profile exists
    /// but whose trades and coverage are gone is NOT repaired on restart. Pinned so a change is noticed.
    /// </summary>
    [Fact]
    public async Task Profile_without_trades_or_coverage_is_left_untouched_on_restart()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            await ExecAsync(db, $"DELETE FROM app.worker_trades WHERE worker_id = (SELECT id FROM app.users WHERE phone='{Marko}'); DELETE FROM app.worker_areas WHERE worker_id = (SELECT id FROM app.users WHERE phone='{Marko}')");
            await SeedAsync(db, admin);
            Assert.Empty(await TradesAsync(db, Marko));
            Assert.Empty(await CoverageIdsAsync(db, Marko));
            Assert.Equal(4, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Profile_for_only_one_worker_exists_other_workers_are_still_seeded_and_the_existing_one_untouched()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await RunStepsAsync(db, admin, typeof(SeedDevUsersStep));
            await ExecAsync(db, $"INSERT INTO app.worker_profiles (user_id, bio, approved_at) SELECT id, 'mine', NULL FROM app.users WHERE phone='{Marko}'");
            await RunStepsAsync(db, admin, typeof(SeedDevWorkersStep));

            Assert.Empty(await TradesAsync(db, Marko));
            Assert.Equal("mine", (await QueryAsync(db, $"SELECT bio FROM app.worker_profiles p JOIN app.users u ON u.id=p.user_id WHERE u.phone='{Marko}'", r => r.GetString(0))).Single());
            Assert.Equal(["Appliance repair", "Plumbing"], await TradesAsync(db, Nikola));
            Assert.Equal(4, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Blocked_worker_stays_blocked_and_untouched_after_restart()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            await ExecAsync(db, $"UPDATE app.users SET blocked_at = '2026-02-03T04:05:06Z' WHERE phone = '{Nikola}'");
            var before = await SnapshotAsync(db);
            await SeedAsync(db, admin);
            Assert.Equal(before, await SnapshotAsync(db));
            Assert.Equal(1, await CountAsync(db, $"SELECT count(*) FROM app.users WHERE phone='{Nikola}' AND blocked_at = '2026-02-03T04:05:06Z'"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Admin_approval_of_stefan_then_restart_keeps_the_approval_time()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            await ExecAsync(db, $"UPDATE app.worker_profiles SET approved_at = '2026-03-04T05:06:07Z' WHERE user_id = (SELECT id FROM app.users WHERE phone='{Stefan}')");
            await SeedAsync(db, admin);
            await SeedAsync(db, admin);
            Assert.Equal(1, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles WHERE approved_at = '2026-03-04T05:06:07Z'"));
            Assert.Equal(3, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles WHERE approved_at IS NOT NULL AND approved_at <> '2026-03-04T05:06:07Z'"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Database_constraints_back_the_seed_duplicates_and_orphans_are_rejected()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());

            // duplicate phone in app.users
            await Assert.ThrowsAnyAsync<PostgresException>(() => ExecAsync(db,
                $"INSERT INTO app.users (id, phone, name, role) VALUES (gen_random_uuid(), '{Marko}', 'Dup', 'Worker')"));
            // duplicate profile / trade / area link
            await Assert.ThrowsAnyAsync<PostgresException>(() => ExecAsync(db,
                "INSERT INTO app.worker_profiles (user_id, bio) SELECT user_id, 'x' FROM app.worker_profiles LIMIT 1"));
            await Assert.ThrowsAnyAsync<PostgresException>(() => ExecAsync(db,
                "INSERT INTO app.worker_trades (worker_id, trade_id) SELECT worker_id, trade_id FROM app.worker_trades LIMIT 1"));
            await Assert.ThrowsAnyAsync<PostgresException>(() => ExecAsync(db,
                "INSERT INTO app.worker_areas (worker_id, area_id) SELECT worker_id, area_id FROM app.worker_areas LIMIT 1"));
            // orphan references
            await Assert.ThrowsAnyAsync<PostgresException>(() => ExecAsync(db,
                "INSERT INTO app.worker_trades (worker_id, trade_id) VALUES (gen_random_uuid(), 1)"));
            await Assert.ThrowsAnyAsync<PostgresException>(() => ExecAsync(db,
                "INSERT INTO app.worker_areas (worker_id, area_id) SELECT user_id, 999999 FROM app.worker_profiles LIMIT 1"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Two_hosts_running_the_workers_step_at_once_never_leave_duplicate_or_half_data()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await RunStepsAsync(db, admin, typeof(SeedDevUsersStep));

            Exception?[] errors = new Exception?[2];
            await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
            {
                try { await RunStepsAsync(db, admin, typeof(SeedDevWorkersStep)); }
                catch (Exception ex) { errors[i] = ex; }
            })));

            // Any loser must fail loudly with a database error (unique violation), never silently corrupt.
            foreach (var e in errors.Where(e => e is not null))
                Assert.Contains("23505", Flatten(e!) + (e!.InnerException as PostgresException)?.SqlState);

            // Final state is exactly one complete seed (all-or-nothing per host).
            Assert.Equal(4, await CountAsync(db, "SELECT count(*) FROM app.worker_profiles"));
            Assert.Equal(7, await CountAsync(db, "SELECT count(*) FROM app.worker_trades"));
            Assert.Equal(4, await CountAsync(db, "SELECT count(DISTINCT worker_id) FROM app.worker_areas"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Trades_and_areas_endpoints_are_unaffected_by_worker_seeding()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await RunStepsAsync(db, admin, typeof(SeedDevUsersStep));
            var tradesBefore = await CountAsync(db, "SELECT count(*) FROM app.trades");
            var areasBefore = await CountAsync(db, "SELECT count(*) FROM app.areas");
            await SeedAsync(db, admin);
            Assert.Equal(tradesBefore, await CountAsync(db, "SELECT count(*) FROM app.trades"));
            Assert.Equal(areasBefore, await CountAsync(db, "SELECT count(*) FROM app.areas"));

            await using var factory = new QaFactory(db.GetConnectionString(), admin, false);
            using var client = factory.CreateClient();
            var trades = await client.GetAsync("/api/trades");
            Assert.Equal(System.Net.HttpStatusCode.OK, trades.StatusCode);
            using var tj = System.Text.Json.JsonDocument.Parse(await trades.Content.ReadAsStringAsync());
            Assert.Equal(tradesBefore, tj.RootElement.GetArrayLength());
            var areas = await client.GetAsync("/api/areas");
            Assert.Equal(System.Net.HttpStatusCode.OK, areas.StatusCode);
            var body = await areas.Content.ReadAsStringAsync();
            Assert.DoesNotContain("worker", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("+3816000001", body);
        }
        finally { await db.DisposeAsync(); }
    }
}
