using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Handyman.Api.Data.Seed;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Serilog.Core;
using Serilog.Events;

namespace Handyman.Api.Tests.Acceptance;

public class US_0003_SeededUsersSignInLocallyTests
{
    /// <summary>In-memory Supabase Auth. Phones are stored the way Supabase does (digits only), so format differences are exercised.</summary>
    private sealed class FakeAuthAdmin : ISupabaseAdminClient
    {
        public Dictionary<string, Guid> ByDigits { get; } = [];
        public int Created;
        public bool Down;

        public Task<Guid> CreateConfirmedPhoneUserAsync(string phone, CancellationToken ct)
        {
            if (Down) throw new HttpRequestException("connection refused");
            var id = Guid.NewGuid();
            ByDigits[Digits(phone)] = id;
            Created++;
            return Task.FromResult(id);
        }

        public Task<Guid?> FindUserIdByPhoneAsync(string phone, CancellationToken ct)
        {
            if (Down) throw new HttpRequestException("connection refused");
            return Task.FromResult<Guid?>(ByDigits.TryGetValue(Digits(phone), out var id) ? id : null);
        }

        public static string Digits(string phone) => new(phone.Where(char.IsAsciiDigit).ToArray());
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent e) { lock (Events) Events.Add(e); }
    }

    private sealed class SeedingFactory(string connectionString, FakeAuthAdmin admin) : WebApplicationFactory<Program>
    {
        public CapturingSink Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            // Wins over appsettings.Local.json, so a developer's local config never reaches these tests.
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Supabase:Url"] = "http://test.local",
                ["Supabase:JwtSecret"] = TestAuth.Secret,
                ["Database:MigrateOnStartup"] = "false",
                ["Seed:Enabled"] = "true",
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILogEventSink>(Logs);
                services.RemoveAll<ISupabaseAdminClient>();
                services.AddSingleton<ISupabaseAdminClient>(admin);
            });
        }
    }

    private sealed record Row(Guid Id, string Phone, string Name, string Role, string? HomeArea);

    private static async Task<List<Row>> ReadUsersAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT u.id, u.phone, u.name, u.role, a.name FROM app.users u LEFT JOIN app.areas a ON a.id = u.home_area_id ORDER BY u.phone", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<Row>();
        while (await r.ReadAsync())
            rows.Add(new Row(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return rows;
    }

    private static async Task<SeedingFactory> StartAsync(TestDatabase db, FakeAuthAdmin admin)
    {
        var factory = new SeedingFactory(db.GetConnectionString(), admin);
        _ = factory.Services; // runs Program.cs startup, including the seeder
        return factory;
    }

    [Fact]
    public async Task AC1_all_ten_users_exist_in_auth_and_in_app_users_with_name_role_and_home_area()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await using var factory = await StartAsync(db, admin);

            var rows = await ReadUsersAsync(db.GetConnectionString());
            Assert.Equal(10, DevUsers.All.Count);
            Assert.Equal(10, rows.Count);
            Assert.Equal(10, admin.ByDigits.Count);
            foreach (var dev in DevUsers.All)
            {
                var row = Assert.Single(rows, r => r.Phone == dev.Phone);
                Assert.Equal(admin.ByDigits[FakeAuthAdmin.Digits(dev.Phone)], row.Id);
                Assert.Equal(dev.Name, row.Name);
                Assert.Equal(dev.Role.ToString(), row.Role);
                Assert.Equal(dev.HomeNeighborhood, row.HomeArea);
            }
            Assert.Equal(5, rows.Count(r => r.Role == "Client" && r.HomeArea is not null));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task AC2_restart_keeps_one_auth_user_and_one_row_per_phone_with_unchanged_ids()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await using (await StartAsync(db, admin)) { }
            var before = await ReadUsersAsync(db.GetConnectionString());

            await using (await StartAsync(db, admin)) { }
            var after = await ReadUsersAsync(db.GetConnectionString());

            Assert.Equal(10, admin.Created);
            Assert.Equal(10, admin.ByDigits.Count);
            Assert.Equal(before, after);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task AC3_existing_auth_user_is_reused_when_no_app_user_exists()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            var existingId = Guid.NewGuid();
            admin.ByDigits["381600000201"] = existingId; // created earlier by a sign-in; stored without '+'

            await using var factory = await StartAsync(db, admin);

            var rows = await ReadUsersAsync(db.GetConnectionString());
            Assert.Equal(9, admin.Created);
            Assert.Equal(10, admin.ByDigits.Count);
            Assert.Equal(existingId, Assert.Single(rows, r => r.Phone == "+381600000201").Id);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Deleted_auth_user_with_kept_app_user_fails_loudly_without_rekeying_or_duplicating()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await using (await StartAsync(db, admin)) { }
            var before = await ReadUsersAsync(db.GetConnectionString());
            admin.ByDigits.Remove("381600000201"); // only Supabase Auth was reset (unsupported)

            var ex = Assert.ThrowsAny<Exception>(() => new SeedingFactory(db.GetConnectionString(), admin).Services);

            Assert.Contains("Reset app.users and Supabase Auth together", Flatten(ex));
            Assert.Equal(before, await ReadUsersAsync(db.GetConnectionString()));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Supabase_down_fails_startup_and_writes_no_app_users()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin { Down = true };

            var ex = Assert.ThrowsAny<Exception>(() => new SeedingFactory(db.GetConnectionString(), admin).Services);

            Assert.Contains("Supabase Auth admin API is not available", Flatten(ex));
            Assert.Empty(await ReadUsersAsync(db.GetConnectionString()));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task AC4_seeded_id_is_returned_by_get_me_for_a_token_with_that_sub()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            var admin = new FakeAuthAdmin();
            await using var factory = await StartAsync(db, admin);
            var anaId = admin.ByDigits["381600000201"];

            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.TokenFor(anaId));
            var response = await client.GetAsync("/api/me");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, Guid>>();
            Assert.Equal(anaId, body!["id"]);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public void AC5_seeded_phones_equal_the_test_otp_numbers_in_supabase_config()
    {
        var configPhones = ReadTestOtpPhones(FindConfigToml());
        var seeded = DevUsers.All.Select(u => DevUsers.NormalizePhone(u.Phone)).Order().ToList();

        Assert.Equal(seeded.Count, seeded.Distinct().Count());
        Assert.Equal(seeded, configPhones.Order().ToList());
    }

    [Theory]
    [InlineData("+381 60 000 0101")]
    [InlineData("381600000101")]
    [InlineData("+381600000101")]
    public void Phone_formats_normalize_to_the_same_number(string raw) =>
        Assert.Equal("+381600000101", DevUsers.NormalizePhone(raw));

    private static List<string> ReadTestOtpPhones(string path)
    {
        var phones = new List<string>();
        var inSection = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.StartsWith("[auth.sms.test_otp]");
                continue;
            }
            if (!inSection || line.StartsWith('#') || line.Length == 0) continue;
            var m = Regex.Match(line, "^[\"']?([^\"'=]+?)[\"']?\\s*=");
            if (m.Success) phones.Add(DevUsers.NormalizePhone(m.Groups[1].Value));
        }
        return phones;
    }

    private static string FindConfigToml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "supabase", "config.toml")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "supabase", "config.toml");
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException) parts.Add(e.Message);
        return string.Join(" | ", parts);
    }
}
