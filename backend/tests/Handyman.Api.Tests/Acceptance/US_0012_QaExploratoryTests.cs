using Handyman.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>
/// US-0012 QA probes beyond the dev tests: migration upgrade over existing rows, concurrency, odd referral-code spellings,
/// rollback gaps. The last test is opt-in against the local Supabase (QA_SUPABASE_DB_URL).
/// </summary>
public class US_0012_QaExploratoryTests : IAsyncLifetime
{
    private const string InitialId = "20261110150844_Initial";
    private const string UniqueId = "20261110162714_UniqueConstraints";
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private AppDbContext Ctx() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_pg.GetConnectionString(), o => o.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)).Options);

    private static string G(int n) => $"00000000-0000-0000-0000-{n:D12}";

    private async Task Exec(string sql)
    {
        await using var c = new NpgsqlConnection(_pg.GetConnectionString());
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> Q(string sql)
    {
        await using var c = new NpgsqlConnection(_pg.GetConnectionString());
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync();
        var l = new List<string>();
        while (await r.ReadAsync()) l.Add(r.GetValue(0)?.ToString() ?? "");
        return l;
    }

    private async Task MigrateTo(string id)
    {
        await using var db = Ctx();
        await db.GetService<IMigrator>().MigrateAsync(id);
    }

    private const string Base = """
        INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count) VALUES
          ('00000000-0000-0000-0000-000000000001', '+381600000201', 'Client', 'C', 0, 0),
          ('00000000-0000-0000-0000-000000000002', '+381600000202', 'Worker', 'W', 0, 0);
        INSERT INTO app.trades (name) VALUES ('T');
        INSERT INTO app.areas (name, kind) VALUES ('A', 'Neighborhood');
        INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, created_at)
        VALUES ('00000000-0000-0000-0000-000000000010', '00000000-0000-0000-0000-000000000001', (SELECT max(id) FROM app.trades), 't', 'd', (SELECT max(id) FROM app.areas), 's', '1', 'Open', now());
        """;

    private static string Bid(int n) => $"""
        INSERT INTO app.bids (id, job_id, worker_id, arrival_window_start, arrival_window_end, diagnostic_fee, hourly_rate, status, created_at)
        VALUES ('{G(n)}', '{G(10)}', '{G(2)}', now(), now() + interval '1 hour', 0, 0, 'Active', now());
        """;

    // Upgrade path: clean existing rows survive and the new rules then apply
    [Fact]
    public async Task Upgrade_over_existing_clean_rows_keeps_data_and_enforces_rules()
    {
        await MigrateTo(InitialId);
        await Exec(Base + Bid(100) + "INSERT INTO app.areas (name, kind) VALUES ('S','Neighborhood'); INSERT INTO app.stores (name, area_id, referral_code) VALUES ('S1', (SELECT max(id) FROM app.areas), 'VRACAR01');");
        var before = (await Q("SELECT number::text FROM app.jobs")).Single();
        await MigrateTo(UniqueId);
        (await Q("SELECT number::text FROM app.jobs")).Single().ShouldBe(before);
        (await Q("SELECT count(*) FROM app.bids")).Single().ShouldBe("1");
        await Should.ThrowAsync<PostgresException>(() => Exec(Bid(101)));
        // the old non-unique index on bids(job_id) is gone, replaced by the composite one (covers job_id lookups)
        (await Q("SELECT indexname FROM pg_indexes WHERE schemaname='app' AND tablename='bids'")).ShouldNotContain("ix_bids_job_id");
    }

    // Upgrade path: pre-existing duplicates make the migration fail, atomically (nothing half-applied)
    [Fact]
    public async Task Upgrade_over_existing_duplicate_bids_fails_atomically()
    {
        await MigrateTo(InitialId);
        await Exec(Base + Bid(100) + Bid(101));
        await Should.ThrowAsync<Exception>(() => MigrateTo(UniqueId));
        (await Q("SELECT \"MigrationId\" FROM app.\"__EFMigrationsHistory\"")).ShouldBe([InitialId]);
        var idx = await Q("SELECT indexname FROM pg_indexes WHERE schemaname='app'");
        idx.ShouldContain("ix_bids_job_id");
        idx.ShouldNotContain("ix_users_phone");
        idx.ShouldNotContain("ux_stores_referral_code_ci");
    }

    // Down migration restores the old indexes
    [Fact]
    public async Task Down_migration_restores_previous_indexes()
    {
        await MigrateTo(UniqueId);
        await MigrateTo(InitialId);
        var idx = await Q("SELECT indexname FROM pg_indexes WHERE schemaname='app'");
        idx.ShouldContain("ix_bids_job_id");
        idx.ShouldContain("ix_ratings_job_id");
        idx.ShouldNotContain("ux_stores_referral_code_ci");
        idx.ShouldNotContain("ix_users_phone");
        await MigrateTo(UniqueId);
        (await Q("SELECT indexname FROM pg_indexes WHERE schemaname='app'")).ShouldContain("ux_stores_referral_code_ci");
    }

    private async Task<string?> SqlState(string sql)
    {
        try { await Exec(sql); return null; }
        catch (PostgresException e) { return e.SqlState; }
    }

    private Task<string?> Store(string code, string name) => SqlState($"""
        INSERT INTO app.areas (name, kind) SELECT 'SA', 'Neighborhood' WHERE NOT EXISTS (SELECT 1 FROM app.areas);
        INSERT INTO app.stores (name, area_id, referral_code) VALUES ('{name}', (SELECT min(id) FROM app.areas), '{code}');
        """);

    // AC4 variants: tab / non-breaking space / non-ASCII letters / internal spaces
    [Theory]
    [InlineData("VRACAR01", "\tvracar01")]       // tab padding
    [InlineData("VRACAR02", "vracar02\u00a0")]   // non-breaking space padding
    [InlineData("VRAČAR03", "vračar03")]         // non-ASCII letter case
    [InlineData("VRACAR 04", "vracar 04")]       // internal space, same case folded
    [InlineData("VRACAR05", "VRACAR05\n")]       // trailing newline
    public async Task Referral_code_spelling_variants_are_refused(string first, string variant)
    {
        await MigrateTo(UniqueId);
        (await Store(first, "F" + first)).ShouldBeNull();
        (await Store(variant, "V" + first)).ShouldBe("23505", $"variant {variant.Replace("\t", "\\t").Replace("\n", "\\n")} slipped through");
    }

    // Concurrency: 10 parallel inserts of the same bid / rating / phone / referral code leave exactly one
    [Fact]
    public async Task Ten_parallel_duplicates_leave_exactly_one_of_each()
    {
        await MigrateTo(UniqueId);
        await Exec(Base);
        var bid = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => SqlState(Bid(500 + i))));
        bid.Count(s => s == null).ShouldBe(1);
        bid.Where(s => s != null).ShouldAllBe(s => s == "23505");

        var rating = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => SqlState($"""
            INSERT INTO app.ratings (id, job_id, rater_id, ratee_id, stars, created_at)
            VALUES ('{G(600 + i)}', '{G(10)}', '{G(1)}', '{G(2)}', {1 + i % 5}, now());
            """)));
        rating.Count(s => s == null).ShouldBe(1);

        var phone = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => SqlState($"""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count)
            VALUES ('{G(700 + i)}', '+381699999999', 'Client', 'P', 0, 0);
            """)));
        phone.Count(s => s == null).ShouldBe(1);

        var store = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Store(i % 2 == 0 ? "RACE01" : "race01", "R" + i)));
        store.Count(s => s == null).ShouldBe(1);
    }

    // AC5: concurrent creates all get distinct numbers; numbers can't be supplied, updated, and rolled-back inserts leave gaps only
    [Fact]
    public async Task Job_numbers_stay_unique_under_parallel_inserts_updates_are_refused_and_rollbacks_leave_gaps()
    {
        await MigrateTo(UniqueId);
        await Exec(Base);
        var res = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => SqlState($"""
            INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, created_at)
            VALUES ('{G(1000 + i)}', '{G(1)}', (SELECT max(id) FROM app.trades), 't', 'd', (SELECT max(id) FROM app.areas), 's', '1', 'Open', now());
            """)));
        res.ShouldAllBe(s => s == null);
        var nums = (await Q("SELECT number::text FROM app.jobs")).Select(int.Parse).ToList();
        nums.Distinct().Count().ShouldBe(nums.Count);
        nums.ShouldAllBe(n => n > 0);

        (await SqlState("UPDATE app.jobs SET number = 999 WHERE id = '" + G(10) + "'")).ShouldBe("428C9");

        var max = nums.Max();
        await using (var c = new NpgsqlConnection(_pg.GetConnectionString()))
        {
            await c.OpenAsync();
            await using var tx = await c.BeginTransactionAsync();
            await using var cmd = new NpgsqlCommand($"""
                INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, created_at)
                VALUES ('{G(2000)}', '{G(1)}', (SELECT max(id) FROM app.trades), 't', 'd', (SELECT max(id) FROM app.areas), 's', '1', 'Open', now());
                """, c, tx);
            await cmd.ExecuteNonQueryAsync();
            await tx.RollbackAsync();
        }
        (await Q("SELECT count(*) FROM app.jobs WHERE id = '" + G(2000) + "'")).Single().ShouldBe("0");
        (await SqlState($"""
            INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, created_at)
            VALUES ('{G(2001)}', '{G(1)}', (SELECT max(id) FROM app.trades), 't', 'd', (SELECT max(id) FROM app.areas), 's', '1', 'Open', now());
            """)).ShouldBeNull();
        int.Parse((await Q("SELECT number::text FROM app.jobs WHERE id = '" + G(2001) + "'")).Single()).ShouldBeGreaterThan(max);
    }

    // Phone: uniqueness is on the stored string only (normalization is E02's job); a NULL phone, if allowed, must not collide
    [Fact]
    public async Task Phone_uniqueness_is_exact_string_match_and_case_of_other_columns_is_irrelevant()
    {
        await MigrateTo(UniqueId);
        (await SqlState("INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count) VALUES ('" + G(1) + "', '+381600000201', 'Client', 'A', 0, 0)")).ShouldBeNull();
        (await SqlState("INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count) VALUES ('" + G(2) + "', '+381600000201', 'Client', 'A', 0, 0)")).ShouldBe("23505");
        (await SqlState("INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count) VALUES ('" + G(3) + "', '+381600000202', 'Client', 'A', 0, 0)")).ShouldBeNull();
    }

    // Opt-in: the indexes exist on the real local Supabase database (needs QA_SUPABASE_DB_URL, Npgsql connection string)
    [Fact]
    public async Task Local_supabase_has_the_unique_indexes_and_the_migration_recorded()
    {
        var url = Environment.GetEnvironmentVariable("QA_SUPABASE_DB_URL");
        if (url is null) return;
        await using var c = new NpgsqlConnection(url);
        await c.OpenAsync();
        async Task<List<string>> L(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            var l = new List<string>();
            while (await r.ReadAsync()) l.Add(r.GetValue(0)?.ToString() ?? "");
            return l;
        }
        (await L("SELECT \"MigrationId\" FROM app.\"__EFMigrationsHistory\"")).ShouldContain(id => id.EndsWith("_UniqueConstraints"));
        var defs = await L("SELECT indexdef FROM pg_indexes WHERE schemaname = 'app'");
        defs.ShouldContain(d => d.Contains("UNIQUE") && d.Contains("ON app.bids") && d.Contains("(job_id, worker_id)"));
        defs.ShouldContain(d => d.Contains("UNIQUE") && d.Contains("ON app.ratings") && d.Contains("(job_id, rater_id)"));
        defs.ShouldContain(d => d.Contains("UNIQUE") && d.Contains("ON app.users") && d.Contains("(phone)"));
        defs.ShouldContain(d => d.Contains("UNIQUE") && d.Contains("ON app.stores") && d.Contains("lower(btrim("));
        defs.ShouldContain(d => d.Contains("UNIQUE") && d.Contains("ON app.jobs") && d.Contains("(number)"));
        (await L("SELECT is_identity || ':' || identity_generation FROM information_schema.columns WHERE table_schema='app' AND table_name='jobs' AND column_name='number'"))
            .Single().ShouldBe("YES:ALWAYS");
    }
}
