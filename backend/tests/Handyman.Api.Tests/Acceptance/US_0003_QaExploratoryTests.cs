using System.Text.RegularExpressions;
using Handyman.Api.Data.Seed;
using Handyman.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>QA attack tests for US-0003 (in-process, fake Supabase admin client).</summary>
public class US_0003_QaExploratoryTests
{
    /// <summary>Thread-safe fake. Like real Supabase, creating a second user with an existing phone fails (HTTP 422 -> HttpRequestException).</summary>
    private sealed class QaAdmin : ISupabaseAdminClient
    {
        private readonly object _gate = new();
        public List<(string Phone, Guid Id)> Users { get; } = [];
        public int Created;
        public int? FailOnCreateNumber; // 1-based: this create call throws
        public int DelayMs;
        private int _createCalls;

        public async Task<Guid> CreateConfirmedPhoneUserAsync(string phone, CancellationToken ct)
        {
            if (DelayMs > 0) await Task.Delay(DelayMs, ct);
            lock (_gate)
            {
                _createCalls++;
                if (FailOnCreateNumber == _createCalls) throw new HttpRequestException("500 simulated");
                if (Users.Any(u => Digits(u.Phone) == Digits(phone))) throw new HttpRequestException("422 phone_exists");
                var id = Guid.NewGuid();
                Users.Add((phone, id));
                Created++;
                return id;
            }
        }

        public async Task<Guid?> FindUserIdByPhoneAsync(string phone, CancellationToken ct)
        {
            if (DelayMs > 0) await Task.Delay(DelayMs, ct);
            lock (_gate)
            {
                var m = Users.Where(u => Digits(u.Phone) == Digits(phone)).ToList();
                return m.Count == 0 ? null : m[0].Id;
            }
        }

        public static string Digits(string p) => new(p.Where(char.IsAsciiDigit).ToArray());
    }

    private sealed class QaFactory(string cs, ISupabaseAdminClient admin) : WebApplicationFactory<Program>
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
                s.AddSingleton(admin);
            });
        }
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException) parts.Add(e.Message);
        return string.Join(" | ", parts);
    }

    private static async Task<List<(Guid Id, string Phone, string Name, string Role, string? Area, string? Parent)>> ReadAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT u.id, u.phone, u.name, u.role, a.name, p.name FROM app.users u LEFT JOIN app.areas a ON a.id=u.home_area_id LEFT JOIN app.areas p ON p.id=a.parent_id ORDER BY u.phone", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<(Guid, string, string, string, string?, string?)>();
        while (await r.ReadAsync())
            list.Add((r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    private static async Task ExecAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Failure_on_fifth_create_fails_startup_writes_nothing_and_retry_completes_without_duplicates()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var cs = db.GetConnectionString();
            var admin = new QaAdmin { FailOnCreateNumber = 5 };

            var ex = Assert.ThrowsAny<Exception>(() => new QaFactory(cs, admin).Services);
            Assert.Contains("Supabase Auth admin API is not available", Flatten(ex));
            Assert.Equal(4, admin.Users.Count);
            Assert.Empty(await ReadAsync(cs));

            admin.FailOnCreateNumber = null;
            await using var ok = new QaFactory(cs, admin);
            _ = ok.Services;

            var rows = await ReadAsync(cs);
            Assert.Equal(10, rows.Count);
            Assert.Equal(10, admin.Users.Count);
            Assert.Equal(10, admin.Created);
            Assert.Equal(10, rows.Select(r => r.Id).Distinct().Count());
            foreach (var r in rows) Assert.Contains(admin.Users, u => u.Id == r.Id && QaAdmin.Digits(u.Phone) == QaAdmin.Digits(r.Phone));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Existing_auth_users_stored_in_other_phone_formats_are_reused()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var cs = db.GetConnectionString();
            var admin = new QaAdmin();
            var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
            admin.Users.Add(("+381 60 000 0101", a));
            admin.Users.Add(("381600000102", b));
            admin.Users.Add(("+381600000999", c));

            await using var f = new QaFactory(cs, admin);
            _ = f.Services;

            var rows = await ReadAsync(cs);
            Assert.Equal(7, admin.Created);
            Assert.Equal(10, admin.Users.Count);
            Assert.Equal(a, rows.Single(r => r.Phone == "+381600000101").Id);
            Assert.Equal(b, rows.Single(r => r.Phone == "+381600000102").Id);
            Assert.Equal(c, rows.Single(r => r.Phone == "+381600000999").Id);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Phone_present_twice_in_auth_picks_one_deterministically_and_creates_one_row()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var cs = db.GetConnectionString();
            var admin = new QaAdmin();
            var first = Guid.NewGuid(); var second = Guid.NewGuid();
            admin.Users.Add(("381600000201", first));
            admin.Users.Add(("+381600000201", second));

            await using var f = new QaFactory(cs, admin);
            _ = f.Services;

            var rows = await ReadAsync(cs);
            Assert.Equal(10, rows.Count);
            Assert.Equal(first, rows.Single(r => r.Phone == "+381600000201").Id);
            Assert.Equal(9, admin.Created);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Two_hosts_seeding_at_once_leave_exactly_ten_consistent_rows()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var cs = db.GetConnectionString();
            // Migrate first so both hosts race only on the seed (migration races are US-0004/platform scope).
            await using (var warm = new QaFactory(cs, new QaAdmin { FailOnCreateNumber = 1 }))
                Assert.ThrowsAny<Exception>(() => warm.Services);

            var admin = new QaAdmin { DelayMs = 30 };
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run<Exception?>(() =>
            {
                try { using var f = new QaFactory(cs, admin); _ = f.Services; return null; }
                catch (Exception e) { return e; }
            })));

            var rows = await ReadAsync(cs);
            // Whatever the winner/loser outcome, the data must never be duplicated or inconsistent.
            Assert.True(rows.Count == 10 || rows.Count == 0, $"rows={rows.Count}");
            Assert.Equal(rows.Count, rows.Select(r => r.Phone).Distinct().Count());
            Assert.True(admin.Users.Count <= 10);
            Assert.Equal(admin.Users.Count, admin.Users.Select(u => QaAdmin.Digits(u.Phone)).Distinct().Count());
            foreach (var r in rows) Assert.Contains(admin.Users, u => u.Id == r.Id);

            // After the race (loser or not), one more start must converge to the full cast.
            await using var again = new QaFactory(cs, admin);
            _ = again.Services;
            Assert.Equal(10, (await ReadAsync(cs)).Count);
            Assert.Equal(10, admin.Users.Count);

            // Record how many hosts survived the race (documented in QA results; not asserted as a requirement).
            Assert.InRange(results.Count(e => e is null), 0, 2);
        }
        finally { await db.DisposeAsync(); }
    }

    // Expected table typed from workflow.md section 7 "Seeded development data", independent of DevUsers.cs.
    private static readonly (string Name, string Phone, string Role, string? Area, string? Parent)[] Expected =
    [
        ("Marko Jovanović", "+381600000101", "Worker", null, null),
        ("Jelena Petrović", "+381600000102", "Worker", null, null),
        ("Nikola Đorđević", "+381600000103", "Worker", null, null),
        ("Stefan Ilić", "+381600000104", "Worker", null, null),
        ("Ana Marković", "+381600000201", "Client", "Čubura", "Vračar"),
        ("Milan Kostić", "+381600000202", "Client", "Bežanijska kosa", "Novi Beograd"),
        ("Ivana Nikolić", "+381600000203", "Client", "Đeram", "Zvezdara"),
        ("Petar Stojanović", "+381600000204", "Client", "Neimar", "Vračar"),
        ("Mila Pavlović", "+381600000205", "Client", "Krunski venac", "Vračar"),
        ("Admin", "+381600000999", "Admin", null, null),
    ];

    [Fact]
    public async Task Seeded_rows_match_workflow_table_exactly_including_admin_and_neighborhood_parent()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var cs = db.GetConnectionString();
            await using var f = new QaFactory(cs, new QaAdmin());
            _ = f.Services;

            var rows = await ReadAsync(cs);
            Assert.Equal(Expected.Length, rows.Count);
            foreach (var e in Expected)
            {
                var r = Assert.Single(rows, x => x.Phone == e.Phone);
                Assert.Equal((e.Name, e.Role, e.Area, e.Parent), (r.Name, r.Role, r.Area, r.Parent));
            }
            Assert.Single(rows, r => r.Role == "Admin");
            Assert.Equal(4, rows.Count(r => r.Role == "Worker"));
            Assert.Equal(5, rows.Count(r => r.Role == "Client"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Seeding_never_touches_existing_non_seeded_rows()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var cs = db.GetConnectionString();
            await using (var f0 = new QaFactory(cs, new QaAdmin { FailOnCreateNumber = 1 }))
                Assert.ThrowsAny<Exception>(() => f0.Services); // migrates, seeds nothing
            var foreignId = Guid.NewGuid();
            await ExecAsync(cs, $"INSERT INTO app.users (id, phone, name, role, rating_sum, rating_count) VALUES ('{foreignId}', '+381699999999', 'Real Person', 'Client', 0, 0)");
            var before = (await ReadAsync(cs)).Single();

            var admin = new QaAdmin();
            await using var f = new QaFactory(cs, admin);
            _ = f.Services;
            await using var f2 = new QaFactory(cs, admin); // restart as well
            _ = f2.Services;

            var rows = await ReadAsync(cs);
            Assert.Equal(11, rows.Count);
            Assert.Equal(before, rows.Single(r => r.Id == foreignId));
            Assert.DoesNotContain(admin.Users, u => QaAdmin.Digits(u.Phone) == "381699999999");
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Existing_row_for_a_seeded_phone_with_another_id_fails_startup_and_is_not_modified_or_duplicated()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var cs = db.GetConnectionString();
            await using (var f0 = new QaFactory(cs, new QaAdmin { FailOnCreateNumber = 1 }))
                Assert.ThrowsAny<Exception>(() => f0.Services);
            var strangerId = Guid.NewGuid();
            await ExecAsync(cs, $"INSERT INTO app.users (id, phone, name, role, rating_sum, rating_count) VALUES ('{strangerId}', '+381600000202', 'Stranger', 'Client', 0, 0)");

            var ex = Assert.ThrowsAny<Exception>(() => new QaFactory(cs, new QaAdmin()).Services);

            Assert.Contains("Reset app.users and Supabase Auth together", Flatten(ex));
            var rows = await ReadAsync(cs);
            var only = Assert.Single(rows);
            Assert.Equal((strangerId, "Stranger"), (only.Id, only.Name));
        }
        finally { await db.DisposeAsync(); }
    }

    // ---- AC5 comparison, exercised on synthetic data so detection of a missing and an extra phone is proven ----

    private static List<string> ParseTestOtp(string toml)
    {
        var phones = new List<string>();
        var inSection = false;
        foreach (var raw in toml.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inSection = line.StartsWith("[auth.sms.test_otp]"); continue; }
            if (!inSection || line.StartsWith('#') || line.Length == 0) continue;
            var m = Regex.Match(line, "^[\"']?([^\"'=]+?)[\"']?\\s*=");
            if (m.Success) phones.Add(DevUsers.NormalizePhone(m.Groups[1].Value));
        }
        return phones;
    }

    private static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) =>
        a.Order().SequenceEqual(b.Order());

    private static string TomlWith(IEnumerable<string> phones) =>
        "[auth.sms]\nenable_signup = true\n\n[auth.sms.test_otp]\n" + string.Join("\n", phones.Select(p => $"{p} = \"123456\"")) + "\n\n[auth.other]\nx = 1\n";

    [Fact]
    public void Ac5_comparison_detects_missing_extra_and_accepts_other_formats()
    {
        var seeded = DevUsers.All.Select(u => DevUsers.NormalizePhone(u.Phone)).ToList();
        var digits = seeded.Select(p => p.TrimStart('+')).ToList();

        Assert.True(SameSet(seeded, ParseTestOtp(TomlWith(digits))));
        Assert.True(SameSet(seeded, ParseTestOtp(TomlWith(seeded.Select(p => "\"" + p + "\"")))));
        Assert.False(SameSet(seeded, ParseTestOtp(TomlWith(digits.Skip(1)))), "missing phone not detected");
        Assert.False(SameSet(seeded, ParseTestOtp(TomlWith(digits.Append("381600000777")))), "extra phone not detected");
        Assert.False(SameSet(seeded, ParseTestOtp(TomlWith(digits.Skip(1).Append("381600000777")))), "swap not detected");
        // Numbers outside the section must not count.
        Assert.False(SameSet(seeded, ParseTestOtp(TomlWith(digits.Skip(1)) + "381600000101 = \"x\"\n")));
    }

    [Fact]
    public void Config_toml_has_the_ten_numbers_without_plus_with_code_123456_and_signup_enabled()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "supabase", "config.toml"))) dir = dir.Parent;
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "supabase", "config.toml"));

        var section = Regex.Match(text, @"\[auth\.sms\.test_otp\]\r?\n((?:(?!\[).*\r?\n?)*)").Groups[1].Value;
        var entries = Regex.Matches(section, @"^\s*(?<k>[^#\s=]+)\s*=\s*""(?<v>[^""]*)""", RegexOptions.Multiline);
        Assert.Equal(10, entries.Count);
        foreach (Match e in entries)
        {
            Assert.Matches("^[0-9]{12}$", e.Groups["k"].Value); // digits only, no '+', as Supabase stores them
            Assert.Equal("123456", e.Groups["v"].Value);
        }
        Assert.True(ParseTestOtp(text).Order().SequenceEqual(
            DevUsers.All.Select(u => DevUsers.NormalizePhone(u.Phone)).Order()));
        Assert.Matches(@"(?ms)\[auth\.sms\].*?enable_signup\s*=\s*true", text);
    }
}
