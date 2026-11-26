using Handyman.Api.Data;
using Handyman.Api.Data.Seed;
using Handyman.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Serilog.Core;
using Serilog.Events;

namespace Handyman.Api.Tests.Acceptance;

public class US_0004_SeederRunsOnlyWhereAllowedTests
{
    private sealed class FakeAdmin : ISupabaseAdminClient
    {
        public int Calls;
        public Task<Guid> CreateConfirmedPhoneUserAsync(string phone, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<Guid?> FindUserIdByPhoneAsync(string phone, CancellationToken ct) => Task.FromResult<Guid?>(null);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
    }

    private sealed class ProbeResult
    {
        public bool Ran;
        public bool SchemaExisted;
    }

    /// <summary>Stands in for the real seed steps: calls the fake admin API and records whether the schema was already migrated.</summary>
    private sealed class ProbeStep(FakeAdmin admin, AppDbContext db, ProbeResult result) : IDevSeedStep
    {
        public async Task SeedAsync(CancellationToken ct)
        {
            result.Ran = true;
            result.SchemaExisted = await db.Users.CountAsync(ct) == 0; // throws when app.users does not exist yet
            await admin.CreateConfirmedPhoneUserAsync("+381600000999", ct);
        }
    }

    private sealed class SeedFactory(string environment, string? seedEnabled, string connectionString, bool migrate)
        : WebApplicationFactory<Program>
    {
        public FakeAdmin Admin { get; } = new();
        public CapturingSink Logs { get; } = new();
        public ProbeResult Probe { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            // Wins over appsettings.Local.json, so a developer's local config never reaches these tests.
            // "Seed:Enabled" is the key the Seed__Enabled environment variable maps to.
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Supabase:Url"] = "http://test.local",
                ["Supabase:JwtSecret"] = TestAuth.Secret,
                ["Database:MigrateOnStartup"] = migrate ? "true" : "false",
                ["Seed:Enabled"] = seedEnabled,
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILogEventSink>(Logs);
                services.RemoveAll<ISupabaseAdminClient>();
                services.AddSingleton(Admin);
                services.AddSingleton<ISupabaseAdminClient>(Admin);
                services.AddSingleton(Probe);
                services.RemoveAll<IDevSeedStep>(); // the real steps are covered by US_0003 tests
                services.AddScoped<IDevSeedStep, ProbeStep>();
            });
        }
    }

    private static async Task<long> CountUsersAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM app.users", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<bool> HasSchemaAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT to_regclass('app.users') IS NOT NULL", conn);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<SeedFactory> StartAsync(TestDatabase db, string environment, string? seedEnabled, bool migrate)
    {
        await db.StartAsync();
        var factory = new SeedFactory(environment, seedEnabled, db.GetConnectionString(), migrate);
        _ = factory.Services; // runs Program.cs startup
        return factory;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    [InlineData("Staging")]
    [InlineData("SomethingElse")]
    public async Task AC1_seeding_is_refused_and_logged_outside_development_and_test(string environment)
    {
        var db = new TestDatabase();
        try
        {
            await using var factory = await StartAsync(db, environment, "true", migrate: true);

            Assert.False(factory.Probe.Ran);
            Assert.Equal(0, factory.Admin.Calls);
            Assert.Equal(0, await CountUsersAsync(db.GetConnectionString()));
            Assert.Contains(factory.Logs.Events, e => e.Level == LogEventLevel.Error
                && e.RenderMessage().Contains("seeding is only allowed in Development and Test"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task AC1_refusal_does_not_even_migrate_an_empty_database(string environment)
    {
        var db = new TestDatabase();
        try
        {
            await using var factory = await StartAsync(db, environment, "true", migrate: false);

            Assert.False(await HasSchemaAsync(db.GetConnectionString()));
            Assert.Equal(0, factory.Admin.Calls);
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("Development", null)]
    [InlineData("Development", "false")]
    [InlineData("Test", null)]
    [InlineData("Test", "false")]
    public async Task AC2_nothing_is_seeded_when_seed_is_disabled_or_missing(string environment, string? seedEnabled)
    {
        var db = new TestDatabase();
        try
        {
            await using var factory = await StartAsync(db, environment, seedEnabled, migrate: true);

            Assert.False(factory.Probe.Ran);
            Assert.Equal(0, factory.Admin.Calls);
            Assert.DoesNotContain(factory.Logs.Events, e => e.RenderMessage().Contains("seed", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, await CountUsersAsync(db.GetConnectionString()));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task AC3_empty_database_is_migrated_first_and_then_seeded(string environment)
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            Assert.False(await HasSchemaAsync(db.GetConnectionString()));

            // Database:MigrateOnStartup is off (Test), so only the seeder can have created the schema before the seed step ran.
            await using var factory = new SeedFactory(environment, "true", db.GetConnectionString(), migrate: false);
            _ = factory.Services;

            Assert.True(await HasSchemaAsync(db.GetConnectionString()));
            Assert.True(factory.Probe.Ran);
            Assert.True(factory.Probe.SchemaExisted);
            Assert.Equal(1, factory.Admin.Calls);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public void AC4_openapi_generation_never_seeds_or_calls_supabase()
    {
        // The build-time generator is recognised by its entry assembly name; Program.cs skips migration and seeding for it.
        var program = File.ReadAllText(FindProgramFile());
        Assert.Contains("GetDocument.Insider", program);
        Assert.Matches(@"if \(!isDocGeneration\)\s+await DevSeedStartup\.RunAsync", program);
        Assert.Contains("if (!isDocGeneration && migrateOnStartup)", program);
    }

    private static string FindProgramFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Handyman.Api", "Program.cs")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Handyman.Api", "Program.cs");
    }
}
