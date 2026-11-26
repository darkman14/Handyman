using Handyman.Api.Data.Seed;
using Handyman.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Handyman.Api.Tests.Acceptance;

public class US_0005_SeededWorkersProfilesTests
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

    private sealed class SeedingFactory(string cs, FakeAuthAdmin admin) : WebApplicationFactory<Program>
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
                ["Seed:Enabled"] = "true",
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
        await using var factory = new SeedingFactory(db.GetConnectionString(), admin);
        _ = factory.Services;
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

    private static async Task<long> CountAsync(TestDatabase db, string sql) =>
        (await QueryAsync(db, sql, r => r.GetInt64(0))).Single();

    private static async Task ExecAsync(TestDatabase db, string sql)
    {
        await using var conn = new NpgsqlConnection(db.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> TradesAsync(TestDatabase db, string phone) =>
        (await QueryAsync(db, $"SELECT t.name FROM app.worker_trades wt JOIN app.users u ON u.id = wt.worker_id JOIN app.trades t ON t.id = wt.trade_id WHERE u.phone = '{phone}' ORDER BY t.name", r => r.GetString(0))).ToArray();

    private static async Task<List<string>> CoverageAsync(TestDatabase db, string phone) =>
        await QueryAsync(db, $"SELECT a.name FROM app.worker_areas wa JOIN app.users u ON u.id = wa.worker_id JOIN app.areas a ON a.id = wa.area_id WHERE u.phone = '{phone}' ORDER BY a.name", r => r.GetString(0));

    private static async Task<List<(string Phone, DateTimeOffset? Approved)>> ProfilesAsync(TestDatabase db) =>
        await QueryAsync(db, "SELECT u.phone, p.approved_at FROM app.worker_profiles p JOIN app.users u ON u.id = p.user_id ORDER BY u.phone",
            r => (r.GetString(0), r.IsDBNull(1) ? (DateTimeOffset?)null : r.GetFieldValue<DateTimeOffset>(1)));

    [Fact]
    public async Task AC1_workers_have_exactly_their_trades()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());

            Assert.Equal(["Electrical", "Heating & AC"], await TradesAsync(db, Marko));
            Assert.Equal(["Appliance repair"], await TradesAsync(db, Jelena));
            Assert.Equal(["Appliance repair", "Plumbing"], await TradesAsync(db, Nikola));
            Assert.Equal(["Carpentry", "Painting"], await TradesAsync(db, Stefan));
            Assert.Equal(7, await CountAsync(db, "SELECT count(*) FROM app.worker_trades"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task AC2_coverage_is_every_neighborhood_of_the_listed_municipalities_and_only_neighborhoods()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());

            foreach (var worker in DevWorkers.All)
            {
                var list = string.Join(",", worker.Municipalities.Select(m => $"'{m}'"));
                var expected = await QueryAsync(db,
                    $"SELECT n.name FROM app.areas n JOIN app.areas m ON m.id = n.parent_id WHERE n.kind = 'Neighborhood' AND m.name = ANY(ARRAY[{list}]) ORDER BY n.name",
                    r => r.GetString(0));
                Assert.NotEmpty(expected);
                Assert.Equal(expected, await CoverageAsync(db, worker.Phone));
            }
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_areas wa JOIN app.areas a ON a.id = wa.area_id WHERE a.kind <> 'Neighborhood'"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task AC3_three_workers_approved_stefan_pending_and_clients_and_admin_have_nothing()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await SeedAsync(db, new FakeAuthAdmin());

            var profiles = await ProfilesAsync(db);
            Assert.Equal([Marko, Jelena, Nikola, Stefan], profiles.Select(p => p.Phone));
            Assert.All(profiles.Where(p => p.Phone != Stefan), p => Assert.NotNull(p.Approved));
            Assert.Null(profiles.Single(p => p.Phone == Stefan).Approved);
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_trades wt JOIN app.users u ON u.id = wt.worker_id WHERE u.role <> 'Worker'"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM app.worker_areas wa JOIN app.users u ON u.id = wa.worker_id WHERE u.role <> 'Worker'"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task AC4_restart_changes_nothing_and_never_approves_stefan()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            var profiles = await ProfilesAsync(db);
            var coverage = await CoverageAsync(db, Nikola);

            await SeedAsync(db, admin);

            Assert.Equal(profiles, await ProfilesAsync(db));
            Assert.Equal(coverage, await CoverageAsync(db, Nikola));
            Assert.Equal(["Appliance repair", "Plumbing"], await TradesAsync(db, Nikola));
            Assert.Equal(7, await CountAsync(db, "SELECT count(*) FROM app.worker_trades"));
            Assert.Null((await ProfilesAsync(db)).Single(p => p.Phone == Stefan).Approved);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Neighborhood_added_to_Vracar_after_first_seed_is_not_added_on_restart()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            var before = await CoverageAsync(db, Jelena);
            await ExecAsync(db, "INSERT INTO app.areas (name, kind, parent_id) SELECT 'Novo naselje', 'Neighborhood', id FROM app.areas WHERE name = 'Vračar' AND kind = 'Municipality'");

            await SeedAsync(db, admin);

            Assert.Equal(before, await CoverageAsync(db, Jelena));
            Assert.DoesNotContain("Novo naselje", await CoverageAsync(db, Stefan));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Admin_approval_made_after_seeding_survives_a_restart()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await SeedAsync(db, admin);
            await ExecAsync(db, $"UPDATE app.worker_profiles SET approved_at = '2026-01-02T03:04:05Z' WHERE user_id = (SELECT id FROM app.users WHERE phone = '{Stefan}')");
            await ExecAsync(db, $"UPDATE app.worker_profiles SET approved_at = NULL WHERE user_id = (SELECT id FROM app.users WHERE phone = '{Marko}')");

            await SeedAsync(db, admin);

            var profiles = await ProfilesAsync(db);
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), profiles.Single(p => p.Phone == Stefan).Approved);
            Assert.Null(profiles.Single(p => p.Phone == Marko).Approved);
        }
        finally { await db.DisposeAsync(); }
    }
}
