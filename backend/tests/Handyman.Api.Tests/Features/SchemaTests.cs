using Handyman.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Handyman.Api.Tests.Features;

/// <summary>US-0011: the migrations create the 13 business tables on a plain Postgres, locked down by RLS.</summary>
public class SchemaFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema))
        .Options);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task<List<string>> QueryStrings(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    public async Task Execute(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

public class SchemaTests(SchemaFixture fx) : IClassFixture<SchemaFixture>
{
    private static readonly string[] ExpectedTables =
    [
        "users", "worker_profiles", "trades", "worker_trades", "areas", "worker_areas", "stores",
        "jobs", "job_photos", "bids", "ratings", "notifications", "device_tokens",
    ];

    private Task<List<string>> Columns(string table) => fx.QueryStrings(
        $"SELECT column_name || ':' || data_type FROM information_schema.columns WHERE table_schema = 'app' AND table_name = '{table}'");

    // AC1
    [Fact]
    public async Task Schema_app_has_exactly_the_13_tables_plus_migrations_history()
    {
        var tables = await fx.QueryStrings("SELECT tablename FROM pg_tables WHERE schemaname = 'app'");
        tables.ShouldBe([.. ExpectedTables, "__EFMigrationsHistory"], ignoreOrder: true);
    }

    // AC1
    [Fact]
    public async Task Tables_have_the_key_fields()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["users"] = ["id", "phone", "role", "name", "home_area_id", "rating_sum", "rating_count", "blocked_at"],
            ["worker_profiles"] = ["user_id", "bio", "approved_at"],
            ["trades"] = ["id", "name"],
            ["worker_trades"] = ["worker_id", "trade_id"],
            ["areas"] = ["id", "name", "kind", "parent_id"],
            ["worker_areas"] = ["worker_id", "area_id"],
            ["stores"] = ["id", "name", "area_id", "referral_code"],
            ["jobs"] = ["id", "number", "client_id", "trade_id", "title", "description", "area_id", "street",
                "house_number", "entrance", "floor", "apartment", "address_note", "store_id", "status",
                "accepted_bid_id", "created_at", "finished_at", "closed_at"],
            ["job_photos"] = ["id", "job_id", "storage_path"],
            ["bids"] = ["id", "job_id", "worker_id", "arrival_window_start", "arrival_window_end",
                "diagnostic_fee", "hourly_rate", "note", "status", "created_at"],
            ["ratings"] = ["id", "job_id", "rater_id", "ratee_id", "stars", "comment", "created_at"],
            ["notifications"] = ["id", "user_id", "type", "job_id", "created_at", "read_at"],
            ["device_tokens"] = ["id", "user_id", "token", "platform"],
        };
        foreach (var (table, columns) in expected)
        {
            var actual = (await Columns(table)).Select(c => c.Split(':')[0]);
            actual.ShouldBe(columns, ignoreOrder: true, customMessage: table);
        }
    }

    // AC1
    [Fact]
    public async Task Times_are_utc_timestamps_and_money_is_integer()
    {
        var bids = await Columns("bids");
        bids.ShouldContain("arrival_window_start:timestamp with time zone");
        bids.ShouldContain("arrival_window_end:timestamp with time zone");
        bids.ShouldContain("diagnostic_fee:integer");
        bids.ShouldContain("hourly_rate:integer");
        (await Columns("jobs")).ShouldContain("created_at:timestamp with time zone");
    }

    // AC1
    [Fact]
    public async Task Users_hold_one_role_in_a_single_column()
    {
        var users = await Columns("users");
        users.Count(c => c.StartsWith("role")).ShouldBe(1);
        users.ShouldNotContain(c => c.StartsWith("roles") || c.StartsWith("is_"));
    }

    // AC1
    [Fact]
    public async Task Bid_arrival_window_end_must_be_after_start()
    {
        await Should.ThrowAsync<PostgresException>(() => fx.Execute("""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count)
            VALUES ('00000000-0000-0000-0000-0000000000a1', '+381600000001', 'Worker', 'W', 0, 0);
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count)
            VALUES ('00000000-0000-0000-0000-0000000000a2', '+381600000002', 'Client', 'C', 0, 0);
            INSERT INTO app.trades (name) VALUES ('Plumber');
            INSERT INTO app.areas (name, kind) VALUES ('Beograd', 'City');
            INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000b1', '00000000-0000-0000-0000-0000000000a2',
                    (SELECT min(id) FROM app.trades), 't', 'd', (SELECT min(id) FROM app.areas), 's', '1', 'Open', now());
            INSERT INTO app.bids (id, job_id, worker_id, arrival_window_start, arrival_window_end, diagnostic_fee, hourly_rate, status, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000b1',
                    '00000000-0000-0000-0000-0000000000a1', now(), now() - interval '1 hour', 0, 0, 'Active', now());
            """));
    }

    // AC2
    [Fact]
    public async Task Every_table_in_schema_app_has_rls_enabled_and_no_policies()
    {
        var withoutRls = await fx.QueryStrings("""
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'app' AND c.relkind IN ('r', 'p') AND NOT c.relrowsecurity
            """);
        withoutRls.ShouldBeEmpty("tables without row-level security");

        var policies = await fx.QueryStrings("SELECT tablename || '.' || policyname FROM pg_policies WHERE schemaname = 'app'");
        policies.ShouldBeEmpty();
    }

    // AC4
    [Fact]
    public async Task No_foreign_key_leaves_schema_app()
    {
        var outside = await fx.QueryStrings("""
            SELECT con.conname FROM pg_constraint con
            JOIN pg_class src ON src.oid = con.conrelid JOIN pg_namespace sn ON sn.oid = src.relnamespace
            JOIN pg_class tgt ON tgt.oid = con.confrelid JOIN pg_namespace tn ON tn.oid = tgt.relnamespace
            WHERE con.contype = 'f' AND (sn.nspname = 'app') AND tn.nspname <> 'app'
            """);
        outside.ShouldBeEmpty();
        // The container is plain Postgres: no Supabase schemas exist, and migrations still applied (fixture).
        (await fx.QueryStrings("SELECT nspname FROM pg_namespace WHERE nspname IN ('auth', 'storage')")).ShouldBeEmpty();
    }

    // AC5
    [Fact]
    public async Task One_first_migration_creates_everything_and_reapplying_changes_nothing()
    {
        await using var db = fx.NewContext();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.Count.ShouldBe(1);
        migrations[0].ShouldEndWith("_Initial");

        var before = await fx.QueryStrings("SELECT \"MigrationId\" FROM app.\"__EFMigrationsHistory\"");
        await db.Database.MigrateAsync(); // simulates an API restart
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        var after = await fx.QueryStrings("SELECT \"MigrationId\" FROM app.\"__EFMigrationsHistory\"");
        after.ShouldBe(before);

        db.Database.HasPendingModelChanges().ShouldBeFalse("model and migration must match");
    }
}
