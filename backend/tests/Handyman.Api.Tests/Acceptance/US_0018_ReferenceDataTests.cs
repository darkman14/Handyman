using Handyman.Api.Data;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>
/// US-0018: trades, areas and the placeholder store come from migrations, in every environment, never from the dev seeder.
/// The class fixture database is only ever read here, so counts are exact.
/// </summary>
public class US_0018_ReferenceDataTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] TradeNames =
        ["Appliance repair", "Electrical", "Plumbing", "Heating & AC", "Painting", "Carpentry"];

    private static readonly Dictionary<string, string[]> Neighborhoods = new()
    {
        ["Vračar"] = ["Crveni krst", "Čubura", "Englezovac", "Kalenić", "Krunski venac", "Neimar"],
        ["Novi Beograd"] = ["Bežanijska kosa", "Fontana", "Ledine", "Paviljoni", "Tošin bunar"],
        ["Zvezdara"] = ["Cvetkova pijaca", "Đeram", "Konjarnik", "Mali Mokri Lug", "Mirijevo"],
    };

    private sealed record Area(int Id, string Name, string Kind, int? ParentId);

    private static async Task<List<object?[]>> Rows(string connectionString, string sql)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await r.ReadAsync())
        {
            var row = new object?[r.FieldCount];
            for (var i = 0; i < r.FieldCount; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private static async Task<long> Count(string cs, string sql) => (long)(await Rows(cs, sql))[0][0]!;

    private static async Task<List<Area>> Areas(string cs) =>
        (await Rows(cs, "SELECT id, name, kind, parent_id FROM app.areas ORDER BY id"))
            .Select(r => new Area((int)r[0]!, (string)r[1]!, (string)r[2]!, (int?)r[3])).ToList();

    private static async Task<List<(int Id, string Name)>> Trades(string cs) =>
        (await Rows(cs, "SELECT id, name FROM app.trades ORDER BY id")).Select(r => ((int)r[0]!, (string)r[1]!)).ToList();

    private static async Task<List<(int Id, string Name, string Code, int AreaId)>> Stores(string cs) =>
        (await Rows(cs, "SELECT id, name, referral_code, area_id FROM app.stores ORDER BY id"))
            .Select(r => ((int)r[0]!, (string)r[1]!, (string)r[2]!, (int)r[3]!)).ToList();

    private string Cs => factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default")!;

    // AC1
    [Fact]
    public async Task There_are_exactly_the_six_trades_in_the_story_order_with_stable_ids()
    {
        var trades = await Trades(Cs);
        trades.Select(t => t.Name).ShouldBe(TradeNames);
        trades.Select(t => t.Id).ShouldBe([1, 2, 3, 4, 5, 6]);
    }

    // AC2
    [Fact]
    public async Task Areas_are_one_city_three_municipalities_and_sixteen_neighborhoods()
    {
        var areas = await Areas(Cs);
        areas.Count.ShouldBe(20);
        var city = areas.Where(a => a.Kind == "City").ShouldHaveSingleItem();
        city.Name.ShouldBe("Beograd");
        city.ParentId.ShouldBeNull();

        var municipalities = areas.Where(a => a.Kind == "Municipality").ToList();
        municipalities.Select(m => m.Name).ShouldBe(["Vračar", "Novi Beograd", "Zvezdara"], ignoreOrder: true);
        municipalities.ShouldAllBe(m => m.ParentId == city.Id);

        foreach (var (municipality, expected) in Neighborhoods)
        {
            var parent = municipalities.Single(m => m.Name == municipality);
            areas.Where(a => a.ParentId == parent.Id).Select(a => a.Name).ShouldBe(expected, ignoreOrder: true);
        }
        areas.Count(a => a.Kind == "Neighborhood").ShouldBe(16);
    }

    [Fact]
    public async Task Every_neighborhood_has_a_municipality_parent_and_no_neighborhood_has_children()
    {
        var areas = await Areas(Cs);
        var byId = areas.ToDictionary(a => a.Id);
        foreach (var n in areas.Where(a => a.Kind == "Neighborhood"))
            byId[n.ParentId!.Value].Kind.ShouldBe("Municipality", n.Name);
        foreach (var m in areas.Where(a => a.Kind == "Municipality"))
            byId[m.ParentId!.Value].Kind.ShouldBe("City", m.Name);
        areas.Where(a => a.Kind == "Neighborhood").ShouldAllBe(n => areas.All(a => a.ParentId != n.Id));
    }

    [Fact]
    public async Task No_area_is_its_own_ancestor()
    {
        var areas = await Areas(Cs);
        var byId = areas.ToDictionary(a => a.Id);
        foreach (var a in areas)
        {
            var seen = new HashSet<int> { a.Id };
            for (var p = a.ParentId; p is not null; p = byId[p.Value].ParentId)
                seen.Add(p.Value).ShouldBeTrue($"{a.Name} is its own ancestor");
        }
    }

    [Fact]
    public async Task Names_keep_their_diacritics_and_the_city_is_not_confused_with_the_municipality()
    {
        var areas = await Areas(Cs);
        foreach (var name in new[] { "Čubura", "Đeram", "Tošin bunar", "Vračar", "Kalenić", "Bežanijska kosa" })
            areas.ShouldContain(a => a.Name == name);
        areas.ShouldNotContain(a => a.Name == "Cubura" || a.Name == "Deram" || a.Name == "Vracar");
        var city = areas.Single(a => a.Name == "Beograd");
        var noviBeograd = areas.Single(a => a.Name == "Novi Beograd");
        city.Kind.ShouldBe("City");
        noviBeograd.Kind.ShouldBe("Municipality");
        noviBeograd.ParentId.ShouldBe(city.Id);
    }

    // AC3
    [Fact]
    public async Task There_is_one_store_Home_Center_Vracar_with_code_VRACAR01_in_the_neighborhood_Cubura()
    {
        var store = (await Stores(Cs)).ShouldHaveSingleItem();
        store.Name.ShouldBe("Home Center Vračar");
        store.Code.ShouldBe("VRACAR01");
        var areas = await Areas(Cs);
        var area = areas.Single(a => a.Id == store.AreaId);
        area.Name.ShouldBe("Čubura");
        area.Kind.ShouldBe("Neighborhood");
        areas.Single(a => a.Id == area.ParentId).Name.ShouldBe("Vračar");
    }

    // Identity sequences were moved past the explicit ids: later inserts must not collide.
    [Fact]
    public async Task Later_inserts_get_fresh_ids_after_the_reference_rows()
    {
        // rolled back, so the fixture database stays untouched
        await using var c = new NpgsqlConnection(Cs);
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        async Task<int> Insert(string sql) { await using var cmd = new NpgsqlCommand(sql, c, tx); return (int)(await cmd.ExecuteScalarAsync())!; }
        var trade = await Insert("INSERT INTO app.trades (name) VALUES ('Roofing') RETURNING id");
        var area = await Insert("INSERT INTO app.areas (name, kind) VALUES ('Zemun', 'City') RETURNING id");
        var store = await Insert($"INSERT INTO app.stores (name, area_id, referral_code) VALUES ('Other', {area}, 'OTHER01') RETURNING id");
        trade.ShouldBe(7);
        area.ShouldBe(21);
        store.ShouldBe(2);
        await tx.RollbackAsync();
    }

    // AC4
    [Fact]
    public async Task Migrating_again_and_restarting_three_times_changes_nothing()
    {
        var before = (await Trades(Cs), await Areas(Cs), await Stores(Cs));
        for (var i = 0; i < 3; i++)
        {
            await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(Cs, o => o.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)).Options))
                await db.Database.MigrateAsync();

            using var restarted = factory.WithWebHostBuilder(b => b.UseSetting("Database:MigrateOnStartup", "true"));
            restarted.CreateClient(); // starting the host runs Migrate() again
        }
        var after = (await Trades(Cs), await Areas(Cs), await Stores(Cs));
        after.Item1.ShouldBe(before.Item1);
        after.Item2.ShouldBe(before.Item2);
        after.Item3.ShouldBe(before.Item3);
        (await Count(Cs, "SELECT count(*) FROM app.trades")).ShouldBe(6);
        (await Count(Cs, "SELECT count(*) FROM app.areas")).ShouldBe(20);
        (await Count(Cs, "SELECT count(*) FROM app.stores")).ShouldBe(1);
    }

    // AC5
    [Theory]
    [InlineData("Production", "false")]
    [InlineData("Production", null)]
    [InlineData("Development", "false")]
    [InlineData("Test", "false")]
    public async Task Reference_data_is_there_when_the_dev_seeder_is_off_missing_or_refused(string environment, string? seedEnabled)
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Database:MigrateOnStartup", "true");
            if (seedEnabled is not null) b.UseSetting("Seed:Enabled", seedEnabled);
        });
        host.CreateClient();
        (await Count(Cs, "SELECT count(*) FROM app.trades")).ShouldBe(6);
        (await Count(Cs, "SELECT count(*) FROM app.areas")).ShouldBe(20);
        (await Count(Cs, "SELECT count(*) FROM app.stores WHERE referral_code = 'VRACAR01'")).ShouldBe(1);
    }

    // AC5: on its own database, migrated by EF alone (no host, so no seeder code can run at all)
    [Fact]
    public async Task A_fresh_database_migrated_by_ef_alone_has_all_reference_data()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        var cs = pg.GetConnectionString();
        await using var db = NewContext(cs);
        await db.Database.MigrateAsync();

        (await Trades(cs)).Select(t => t.Name).ShouldBe(TradeNames);
        (await Areas(cs)).Count.ShouldBe(20);
        (await Stores(cs)).Single().Code.ShouldBe("VRACAR01");
    }

    private static AppDbContext NewContext(string cs) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(cs, o => o.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)).Options);

    // Upgrade path: a database with the earlier migrations applied and some data of its own.
    private const string BeforeId = "20261124164959_AddXminConcurrencyTokens";

    [Fact]
    public async Task Upgrade_keeps_existing_rows_adopts_matching_ones_and_never_duplicates()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        var cs = pg.GetConnectionString();
        await using var db = NewContext(cs);
        await db.GetService<IMigrator>().MigrateAsync(BeforeId);

        // an environment that already has: Plumbing (id 40), a custom trade, the city Beograd (id 30) and an unrelated area sitting on reference id 5
        await using (var c = new NpgsqlConnection(cs))
        {
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO app.trades (id, name) OVERRIDING SYSTEM VALUE VALUES (40, 'Plumbing'), (41, 'Roofing');
                INSERT INTO app.areas (id, name, kind) OVERRIDING SYSTEM VALUE VALUES (30, 'Beograd', 'City'), (5, 'Junk', 'City');
                """, c);
            await cmd.ExecuteNonQueryAsync();
        }

        await db.Database.MigrateAsync();

        var trades = await Trades(cs);
        trades.Count.ShouldBe(7);
        trades.Single(t => t.Name == "Plumbing").Id.ShouldBe(40); // adopted, not duplicated
        trades.Single(t => t.Name == "Roofing").Id.ShouldBe(41);   // untouched
        var areas = await Areas(cs);
        areas.Single(a => a.Name == "Beograd").Id.ShouldBe(30);
        areas.Where(a => a.Kind == "Municipality").ShouldAllBe(m => m.ParentId == 30);
        areas.Count.ShouldBe(21); // 20 reference (Beograd adopted, not added twice) + the junk area
        areas.Single(a => a.Name == "Junk").Id.ShouldBe(5); // untouched; its id clashed with 'Crveni krst', which took the next free id
        areas.Single(a => a.Name == "Crveni krst").Id.ShouldBeGreaterThan(30);
        (await Stores(cs)).Single().Code.ShouldBe("VRACAR01");

        // new rows do not collide with the explicit ids
        await using var c2 = new NpgsqlConnection(cs);
        await c2.OpenAsync();
        await using var ins = new NpgsqlCommand("INSERT INTO app.trades (name) VALUES ('Tiling') RETURNING id", c2);
        ((int)(await ins.ExecuteScalarAsync())!).ShouldBeGreaterThan(41);
    }

    [Fact]
    public async Task Rolling_back_the_migration_removes_only_the_reference_rows()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        var cs = pg.GetConnectionString();
        await using var db = NewContext(cs);
        await db.Database.MigrateAsync();
        await db.GetService<IMigrator>().MigrateAsync(BeforeId);

        (await Count(cs, "SELECT count(*) FROM app.trades")).ShouldBe(0);
        (await Count(cs, "SELECT count(*) FROM app.areas")).ShouldBe(0);
        (await Count(cs, "SELECT count(*) FROM app.stores")).ShouldBe(0);
        await db.Database.MigrateAsync();
        (await Count(cs, "SELECT count(*) FROM app.areas")).ShouldBe(20);
    }

    [Fact]
    public async Task The_model_has_no_pending_changes()
    {
        await using var db = NewContext(Cs);
        db.GetService<IMigrationsAssembly>().ModelSnapshot.ShouldNotBeNull();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
