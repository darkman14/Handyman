using System.Text;
using Handyman.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>
/// US-0018 QA: the ReferenceData migration on throwaway databases (one Postgres 17 container per test, one database in it):
/// fresh content byte for byte, id clashes, same-name rows, colliding referral codes, referenced rows, Down/Up.
/// KNOWN_GAP tests pin today's behavior of a confirmed bug and pass; flip them when the bug is fixed.
/// </summary>
public class US_0018_QaReferenceDataMigrationTests : IAsyncLifetime
{
    private const string Before = "20261124164959_AddXminConcurrencyTokens";
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private static int _db;

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private static AppDbContext Ctx(string cs) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(cs, o => o.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)).Options);

    /// <summary>New empty database; returns its connection string.</summary>
    private async Task<string> NewDb()
    {
        var name = $"qa{Interlocked.Increment(ref _db)}";
        await using (var c = new NpgsqlConnection(_pg.GetConnectionString()))
        {
            await c.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE {name}", c).ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(_pg.GetConnectionString()) { Database = name, Pooling = false }.ConnectionString;
    }

    private static Task MigrateTo(string cs, string id) => Ctx(cs).GetService<IMigrator>().MigrateAsync(id);

    private static async Task Exec(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        await new NpgsqlCommand(sql, c).ExecuteNonQueryAsync();
    }

    private static async Task<List<object?[]>> Q(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        await using var r = await new NpgsqlCommand(sql, c).ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await r.ReadAsync())
        {
            var row = new object?[r.FieldCount];
            for (var i = 0; i < r.FieldCount; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private static async Task<long> Count(string cs, string sql) => (long)(await Q(cs, sql))[0][0]!;

    private static string Hex(string s) => Convert.ToHexString(Encoding.UTF8.GetBytes(s)).ToLowerInvariant();

    private static string G(int n) => $"00000000-0000-0000-0000-{n:D12}";

    // Expected: id, name, kind, parent id (story, business rules)
    private static readonly (int Id, string Name, string Kind, int? Parent)[] ExpectedAreas =
    [
        (1, "Beograd", "City", null),
        (2, "Vračar", "Municipality", 1), (3, "Novi Beograd", "Municipality", 1), (4, "Zvezdara", "Municipality", 1),
        (5, "Crveni krst", "Neighborhood", 2), (6, "Čubura", "Neighborhood", 2), (7, "Englezovac", "Neighborhood", 2),
        (8, "Kalenić", "Neighborhood", 2), (9, "Krunski venac", "Neighborhood", 2), (10, "Neimar", "Neighborhood", 2),
        (11, "Bežanijska kosa", "Neighborhood", 3), (12, "Fontana", "Neighborhood", 3), (13, "Ledine", "Neighborhood", 3),
        (14, "Paviljoni", "Neighborhood", 3), (15, "Tošin bunar", "Neighborhood", 3),
        (16, "Cvetkova pijaca", "Neighborhood", 4), (17, "Đeram", "Neighborhood", 4), (18, "Konjarnik", "Neighborhood", 4),
        (19, "Mali Mokri Lug", "Neighborhood", 4), (20, "Mirijevo", "Neighborhood", 4),
    ];

    // AC1 AC2 AC3: every byte of every name, id, kind and parent on a fresh database
    [Fact]
    public async Task Fresh_database_has_exactly_the_expected_rows_byte_for_byte()
    {
        var cs = await NewDb();
        await Ctx(cs).Database.MigrateAsync();

        var trades = await Q(cs, "SELECT id, encode(convert_to(name,'UTF8'),'hex') FROM app.trades ORDER BY id");
        trades.Select(r => ((int)r[0]!, (string)r[1]!)).ToList().ShouldBe(
            new[] { "Appliance repair", "Electrical", "Plumbing", "Heating & AC", "Painting", "Carpentry" }
                .Select((n, i) => (i + 1, Hex(n))).ToList());

        var areas = await Q(cs, "SELECT id, encode(convert_to(name,'UTF8'),'hex'), kind, parent_id FROM app.areas ORDER BY id");
        areas.Select(r => ((int)r[0]!, (string)r[1]!, (string)r[2]!, (int?)r[3])).ToList()
            .ShouldBe(ExpectedAreas.Select(a => (a.Id, Hex(a.Name), a.Kind, a.Parent)).ToList());
        // precomposed (NFC) forms only
        foreach (var a in ExpectedAreas) a.Name.IsNormalized(NormalizationForm.FormC).ShouldBeTrue(a.Name);

        var store = await Q(cs, "SELECT id, encode(convert_to(name,'UTF8'),'hex'), referral_code, area_id FROM app.stores");
        store.Count.ShouldBe(1);
        store[0].ShouldBe([1, Hex("Home Center Vračar"), "VRACAR01", 6]);

        // sequences: next ids follow the reference rows
        (await Q(cs, "INSERT INTO app.trades (name) VALUES ('x') RETURNING id"))[0][0].ShouldBe(7);
        (await Q(cs, "INSERT INTO app.areas (name, kind) VALUES ('x','City') RETURNING id"))[0][0].ShouldBe(21);
        (await Q(cs, "INSERT INTO app.stores (name, area_id, referral_code) VALUES ('x', 1, 'X1') RETURNING id"))[0][0].ShouldBe(2);
    }

    // The migration adds data only: no policy, no constraint, index or column, RLS still on everywhere
    [Fact]
    public async Task Rls_is_on_for_every_table_no_policies_and_no_constraint_or_index_added_by_the_migration()
    {
        var before = await NewDb();
        await MigrateTo(before, Before);
        var after = await NewDb();
        await Ctx(after).Database.MigrateAsync();

        var rls = await Q(after, "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='app' AND c.relkind='r' AND NOT c.relrowsecurity");
        rls[0][0].ShouldBe(0L);
        (await Count(after, "SELECT count(*) FROM pg_policies")).ShouldBe(0);

        const string schemaSql = "SELECT 'c:' || conrelid::regclass || ':' || conname FROM pg_constraint WHERE connamespace = 'app'::regnamespace " +
                                 "UNION ALL SELECT 'i:' || indexname FROM pg_indexes WHERE schemaname='app' " +
                                 "UNION ALL SELECT 'col:' || table_name || '.' || column_name FROM information_schema.columns WHERE table_schema='app' ORDER BY 1";
        (await Q(after, schemaSql)).Select(r => (string)r[0]!).ShouldBe((await Q(before, schemaSql)).Select(r => (string)r[0]!));
    }

    // AC4
    [Fact]
    public async Task Migrate_is_idempotent_and_ids_do_not_change()
    {
        var cs = await NewDb();
        await Ctx(cs).Database.MigrateAsync();
        async Task<string> Snap() => string.Join("|", (await Q(cs, "SELECT 'a'||id||name||kind||coalesce(parent_id,0) FROM app.areas UNION ALL SELECT 't'||id||name FROM app.trades UNION ALL SELECT 's'||id||name||area_id||referral_code FROM app.stores ORDER BY 1")).Select(r => r[0]));
        var first = await Snap();
        for (var i = 0; i < 3; i++) await Ctx(cs).Database.MigrateAsync();
        (await Snap()).ShouldBe(first);
    }

    // Id clash: unrelated rows own ids 1..6 / 1..20 / store 1
    [Fact]
    public async Task Unrelated_rows_on_the_reference_ids_stay_and_the_reference_rows_get_other_ids()
    {
        var cs = await NewDb();
        await MigrateTo(cs, Before);
        await Exec(cs, """
            INSERT INTO app.trades (id, name) OVERRIDING SYSTEM VALUE SELECT g, 'Junk trade ' || g FROM generate_series(1, 6) g;
            INSERT INTO app.areas (id, name, kind) OVERRIDING SYSTEM VALUE SELECT g, 'Junk area ' || g, 'Neighborhood' FROM generate_series(1, 20) g;
            INSERT INTO app.stores (id, name, area_id, referral_code) OVERRIDING SYSTEM VALUE VALUES (1, 'Foreign store', 1, 'FOREIGN1');
            """);
        await Ctx(cs).Database.MigrateAsync();

        (await Count(cs, "SELECT count(*) FROM app.trades")).ShouldBe(12);
        (await Count(cs, "SELECT count(*) FROM app.trades WHERE id <= 6 AND name LIKE 'Junk trade %'")).ShouldBe(6);
        (await Count(cs, "SELECT count(*) FROM app.areas")).ShouldBe(40);
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE id <= 20 AND name LIKE 'Junk area %' AND parent_id IS NULL")).ShouldBe(20);
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE name IN ('Beograd','Čubura','Đeram')")).ShouldBe(3);
        (await Count(cs, "SELECT count(*) FROM app.areas a WHERE a.kind='Neighborhood' AND a.id > 20 AND a.parent_id IS NOT NULL")).ShouldBe(16);
        (await Count(cs, "SELECT count(*) FROM app.stores")).ShouldBe(2);
        var store = (await Q(cs, "SELECT s.id, a.name, p.name FROM app.stores s JOIN app.areas a ON a.id=s.area_id JOIN app.areas p ON p.id=a.parent_id WHERE s.referral_code='VRACAR01'")).Single();
        store[0].ShouldNotBe(1);
        store[1].ShouldBe("Čubura");
        store[2].ShouldBe("Vračar");
        (await Q(cs, "SELECT name FROM app.stores WHERE id=1"))[0][0].ShouldBe("Foreign store");
        // next inserts work
        await Exec(cs, "INSERT INTO app.trades (name) VALUES ('n'); INSERT INTO app.areas (name, kind) VALUES ('n','City'); INSERT INTO app.stores (name, area_id, referral_code) VALUES ('n', 1, 'NEXT01');");
    }

    [Theory]
    [InlineData("vracar01")]
    [InlineData("  VRACAR01 ")]
    [InlineData("VRA CAR01")]
    [InlineData("VRACAR 01")]
    [InlineData("Vracar\t01")]
    public async Task An_existing_store_with_the_code_in_another_spelling_is_respected_and_no_second_store_is_added(string existing)
    {
        var cs = await NewDb();
        await MigrateTo(cs, Before);
        await Exec(cs, "INSERT INTO app.areas (id, name, kind) OVERRIDING SYSTEM VALUE VALUES (1, 'Other', 'City');");
        await using (var c = new NpgsqlConnection(cs))
        {
            await c.OpenAsync();
            var cmd = new NpgsqlCommand("INSERT INTO app.stores (name, area_id, referral_code) VALUES ('Mine', 1, @c)", c);
            cmd.Parameters.AddWithValue("c", existing);
            await cmd.ExecuteNonQueryAsync();
        }
        await Ctx(cs).Database.MigrateAsync();
        (await Count(cs, "SELECT count(*) FROM app.stores")).ShouldBe(1);
        (await Q(cs, "SELECT name FROM app.stores"))[0][0].ShouldBe("Mine");
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE kind='City' AND name='Beograd'")).ShouldBe(1); // area tree still complete
    }

    [Fact]
    public async Task Rows_with_the_same_name_in_the_right_place_are_adopted_not_duplicated_and_foreign_keys_stay_valid()
    {
        var cs = await NewDb();
        await MigrateTo(cs, Before);
        await Exec(cs, $"""
            INSERT INTO app.trades (id, name) OVERRIDING SYSTEM VALUE VALUES (50, 'Electrical'), (51, 'Plumbing');
            INSERT INTO app.areas (id, name, kind, parent_id) OVERRIDING SYSTEM VALUE VALUES (60, 'Beograd', 'City', NULL), (61, 'Vračar', 'Municipality', 60), (62, 'Čubura', 'Neighborhood', 61);
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count, home_area_id) VALUES ('{G(1)}', '+381600000001', 'Client', 'C', 0, 0, 62), ('{G(2)}', '+381600000002', 'Worker', 'W', 0, 0, 61);
            INSERT INTO app.worker_areas (worker_id, area_id) VALUES ('{G(2)}', 62);
            INSERT INTO app.worker_trades (worker_id, trade_id) VALUES ('{G(2)}', 50);
            INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, created_at)
              VALUES ('{G(3)}', '{G(1)}', 51, 't', 'd', 62, 's', '1', 'Open', now());
            """);
        await Ctx(cs).Database.MigrateAsync();
        (await Count(cs, "SELECT count(*) FROM app.trades")).ShouldBe(6);
        (await Count(cs, "SELECT count(*) FROM app.areas")).ShouldBe(20);
        (await Count(cs, "SELECT count(*) FROM app.trades WHERE id IN (50,51)")).ShouldBe(2);
        (await Q(cs, "SELECT s.area_id FROM app.stores s"))[0][0].ShouldBe(62);
        (await Count(cs, "SELECT count(*) FROM app.areas a WHERE a.parent_id = 61")).ShouldBe(6);
    }

    [Fact]
    public async Task A_foreign_municipality_named_Vracar_under_another_city_does_not_abort_and_stays_untouched()
    {
        var cs = await NewDb();
        await MigrateTo(cs, Before);
        await Exec(cs, """
            INSERT INTO app.areas (id, name, kind, parent_id) OVERRIDING SYSTEM VALUE VALUES (100, 'Nis', 'City', NULL), (101, 'Vračar', 'Municipality', 100);
            """);
        await Ctx(cs).Database.MigrateAsync();
        (await Count(cs, "SELECT count(*) FROM app.areas")).ShouldBe(22); // 2 foreign + 20 reference
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE id IN (100,101) AND parent_id IS NOT DISTINCT FROM CASE id WHEN 101 THEN 100 END")).ShouldBe(2);
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE parent_id = 101")).ShouldBe(0); // foreign Vračar gets no children
        var store = (await Q(cs, "SELECT p.parent_id FROM app.stores s JOIN app.areas a ON a.id=s.area_id JOIN app.areas p ON p.id=a.parent_id WHERE a.name='Čubura'"))[0][0];
        store.ShouldNotBe(100);
        (await Count(cs, "SELECT count(*) FROM app.trades")).ShouldBe(6);
    }

    [Fact]
    public async Task Two_existing_cities_named_Beograd_do_not_abort_and_the_lowest_id_is_adopted()
    {
        var cs = await NewDb();
        await MigrateTo(cs, Before);
        await Exec(cs, "INSERT INTO app.areas (id, name, kind) OVERRIDING SYSTEM VALUE VALUES (100, 'Beograd', 'City'), (101, 'Beograd', 'City');");
        await Ctx(cs).Database.MigrateAsync();
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE kind='City'")).ShouldBe(2); // no third city
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE parent_id = 100")).ShouldBe(3); // tree hangs under the lowest id
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE parent_id = 101")).ShouldBe(0);
        (await Count(cs, "SELECT count(*) FROM app.areas")).ShouldBe(21);
        (await Count(cs, "SELECT count(*) FROM app.trades")).ShouldBe(6);
        (await Count(cs, "SELECT count(*) FROM app.stores")).ShouldBe(1);
    }

    [Fact]
    public async Task A_foreign_Cubura_neighborhood_elsewhere_does_not_break_the_migration_and_the_store_is_in_Vracar_Cubura()
    {
        var cs = await NewDb();
        await MigrateTo(cs, Before);
        await Exec(cs, """
            INSERT INTO app.areas (id, name, kind, parent_id) OVERRIDING SYSTEM VALUE VALUES (100, 'Nis', 'City', NULL), (101, 'Centar', 'Municipality', 100), (102, 'Čubura', 'Neighborhood', 101);
            """);
        await Ctx(cs).Database.MigrateAsync();
        var parent = (await Q(cs, "SELECT p.name FROM app.stores s JOIN app.areas a ON a.id=s.area_id JOIN app.areas p ON p.id=a.parent_id"))[0][0];
        parent.ShouldBe("Vračar");
    }

    [Fact]
    public async Task Names_are_matched_exactly_so_a_differently_cased_trade_is_a_different_row()
    {
        // documents behavior: no case folding or Unicode normalization when adopting existing rows
        var cs = await NewDb();
        await MigrateTo(cs, Before);
        await Exec(cs, "INSERT INTO app.trades (id, name) OVERRIDING SYSTEM VALUE VALUES (50, 'plumbing');");
        await Ctx(cs).Database.MigrateAsync();
        (await Count(cs, "SELECT count(*) FROM app.trades WHERE lower(name)='plumbing'")).ShouldBe(2);
    }

    [Fact]
    public async Task Rows_referenced_by_other_tables_keep_their_ids_across_a_second_migrate()
    {
        var cs = await NewDb();
        await Ctx(cs).Database.MigrateAsync();
        await Exec(cs, $"""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count, home_area_id) VALUES ('{G(1)}', '+381600000001', 'Client', 'C', 0, 0, 6);
            INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, store_id, created_at)
              VALUES ('{G(3)}', '{G(1)}', 3, 't', 'd', 6, 's', '1', 'Open', 1, now());
            """);
        await Ctx(cs).Database.MigrateAsync();
        (await Q(cs, "SELECT trade_id, area_id, store_id FROM app.jobs"))[0].ShouldBe([3, 6, 1]);
    }

    // US-0012 AC4 on the real migrated data: the seeded code cannot be registered again
    [Theory]
    [InlineData("VRACAR01")]
    [InlineData("vracar01")]
    [InlineData("  Vracar01 ")]
    [InlineData("VRA CAR 01")]
    [InlineData("VRACAR 01")]
    [InlineData("\tVRACAR01\n")]
    public async Task The_migrated_store_code_cannot_be_registered_again_in_any_case_or_spacing(string again)
    {
        var cs = await NewDb();
        await Ctx(cs).Database.MigrateAsync();
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        var cmd = new NpgsqlCommand("INSERT INTO app.stores (name, area_id, referral_code) VALUES ('Dup', 6, @c)", c);
        cmd.Parameters.AddWithValue("c", again);
        var ex = await Should.ThrowAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        ex.SqlState.ShouldBe("23505");
    }

    // Down / Up
    [Fact]
    public async Task Down_with_referenced_rows_keeps_them_and_does_not_fail_and_Up_again_adopts_them()
    {
        var cs = await NewDb();
        await Ctx(cs).Database.MigrateAsync();
        await Exec(cs, $"""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count, home_area_id) VALUES ('{G(1)}', '+381600000001', 'Client', 'C', 0, 0, 6), ('{G(2)}', '+381600000002', 'Worker', 'W', 0, 0, NULL);
            INSERT INTO app.worker_trades (worker_id, trade_id) VALUES ('{G(2)}', 2);
            INSERT INTO app.worker_areas (worker_id, area_id) VALUES ('{G(2)}', 17);
            INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, store_id, created_at)
              VALUES ('{G(3)}', '{G(1)}', 3, 't', 'd', 12, 's', '1', 'Open', 1, now());
            """);
        await MigrateTo(cs, Before);

        // referenced rows stay: trades 2 and 3, areas 6, 12, 17, store 1; unreferenced trades go
        (await Count(cs, "SELECT count(*) FROM app.trades WHERE id IN (2,3)")).ShouldBe(2);
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE id IN (6,12,17)")).ShouldBe(3);
        (await Count(cs, "SELECT count(*) FROM app.stores WHERE id = 1")).ShouldBe(1);
        (await Count(cs, "SELECT count(*) FROM app.trades WHERE id IN (1,4,5,6)")).ShouldBe(0);

        await Ctx(cs).Database.MigrateAsync();
        (await Count(cs, "SELECT count(*) FROM app.trades")).ShouldBe(6);
        (await Count(cs, "SELECT count(*) FROM app.areas")).ShouldBe(20);
        (await Count(cs, "SELECT count(*) FROM app.stores")).ShouldBe(1);
        (await Q(cs, "SELECT trade_id, area_id, store_id FROM app.jobs"))[0].ShouldBe([3, 12, 1]);
    }

    /// <summary>KNOWN_GAP_BUG_0012: flip to ShouldBe(1) when fixed (Down must match the name too).</summary>
    [Fact]
    public async Task KNOWN_GAP_BUG_0012_down_deletes_a_foreign_neighborhood_that_sits_on_a_reference_id()
    {
        var cs = await NewDb();
        await MigrateTo(cs, Before);
        await Exec(cs, "INSERT INTO app.areas (id, name, kind) OVERRIDING SYSTEM VALUE VALUES (7, 'Foreign hood', 'Neighborhood');");
        await Ctx(cs).Database.MigrateAsync();
        await MigrateTo(cs, Before);
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE name = 'Foreign hood'")).ShouldBe(0);
    }

    /// <summary>KNOWN_GAP_BUG_0012: flip to ex.ShouldBeNull() when fixed.</summary>
    [Fact]
    public async Task KNOWN_GAP_BUG_0012_down_fails_when_a_foreign_area_is_a_child_of_a_reference_neighborhood()
    {
        var cs = await NewDb();
        await Ctx(cs).Database.MigrateAsync();
        await Exec(cs, "INSERT INTO app.areas (name, kind, parent_id) VALUES ('Block A', 'Neighborhood', 6);");
        var ex = await Record.ExceptionAsync(() => MigrateTo(cs, Before));
        ((PostgresException)ex!).SqlState.ShouldBe("23503");
        (await Count(cs, "SELECT count(*) FROM app.areas WHERE name = 'Block A'")).ShouldBe(1); // nothing lost, transaction rolled back
    }

    [Fact]
    public async Task Down_never_deletes_foreign_trades_that_sit_on_the_reference_ids()
    {
        // trades 1..6 owned by junk: the reference trades got ids 7..12; Down must not delete the junk
        var cs = await NewDb();
        await MigrateTo(cs, Before);
        await Exec(cs, "INSERT INTO app.trades (id, name) OVERRIDING SYSTEM VALUE SELECT g, 'Plumbing' || g FROM generate_series(1,6) g;");
        await Ctx(cs).Database.MigrateAsync();
        await MigrateTo(cs, Before);
        (await Count(cs, "SELECT count(*) FROM app.trades WHERE name LIKE 'Plumbing_'")).ShouldBe(6);
    }
}
