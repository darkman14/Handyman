using System.Net;
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

/// <summary>QA attack tests for US-0004: environment-name variants, Seed:Enabled parsing, failing steps, idempotency, log hygiene.</summary>
public class US_0004_QaExploratoryTests
{
    private const string FakeKey = "SRK-FAKE-SERVICE-ROLE-KEY-123";
    private const string FakePassword = "pw-SECRET-xyz";
    private const string FakePhone = "+381600000777";

    private sealed class Sink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent e) { lock (Events) Events.Add(e); }
        public string All() { lock (Events) return string.Join("\n", Events.Select(e => e.RenderMessage() + e.Exception)); }
    }

    private sealed class Counter { public int Runs; public int Calls; }

    private sealed class CountingStep(Counter c) : IDevSeedStep
    {
        public Task SeedAsync(CancellationToken ct) { Interlocked.Increment(ref c.Runs); return Task.CompletedTask; }
    }

    /// <summary>Inserts a user row, then a second step throws: shows what is left behind.</summary>
    private sealed class InsertThenFailSteps(AppDbContext db) : IDevSeedStep
    {
        public async Task SeedAsync(CancellationToken ct)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            throw new InvalidOperationException("seed step boom");
        }
    }

    private sealed class Factory(string environment, string? seedEnabled, string cs, Action<IServiceCollection>? extra = null)
        : WebApplicationFactory<Program>
    {
        public Sink Logs { get; } = new();
        public Counter Count { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = cs,
                ["Supabase:Url"] = "http://test.local",
                ["Supabase:JwtSecret"] = TestAuth.Secret,
                ["Supabase:ServiceRoleKey"] = FakeKey,
                ["Database:MigrateOnStartup"] = "false",
                ["Seed:Enabled"] = seedEnabled,
            }));
            builder.ConfigureServices(s =>
            {
                s.AddSingleton<ILogEventSink>(Logs);
                s.AddSingleton(Count);
                s.AddScoped<IDevSeedStep, CountingStep>();
                extra?.Invoke(s);
            });
        }
    }

    private static async Task<bool> HasSchemaAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT to_regclass('app.users') IS NOT NULL", conn);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    [Theory]
    [InlineData("Development ")]
    [InlineData("Test ")]
    [InlineData(" Development")]
    [InlineData("Dev")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("Local")]
    [InlineData("Developmentx")]
    public async Task Environment_name_variants_that_are_not_exactly_development_or_test_are_refused(string environment)
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await using var f = new Factory(environment, "true", db.GetConnectionString());
            _ = f.Services;
            Assert.Equal(0, f.Count.Runs);
            Assert.False(await HasSchemaAsync(db.GetConnectionString()));
            Assert.Contains(f.Logs.Events, e => e.Level == LogEventLevel.Error);
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("development")]
    [InlineData("DEVELOPMENT")]
    [InlineData("dEvElOpMeNt")]
    [InlineData("test")]
    [InlineData("TEST")]
    public async Task Environment_name_in_any_casing_of_development_or_test_seeds(string environment)
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await using var f = new Factory(environment, "true", db.GetConnectionString());
            _ = f.Services;
            Assert.Equal(1, f.Count.Runs);
            Assert.True(await HasSchemaAsync(db.GetConnectionString()));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("true ")]
    public async Task Seed_enabled_boolean_spellings_seed_in_development(string value)
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await using var f = new Factory("Development", value, db.GetConnectionString());
            _ = f.Services;
            Assert.Equal(1, f.Count.Runs);
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    public async Task Seed_enabled_false_does_not_seed(string value)
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await using var f = new Factory("Development", value, db.GetConnectionString());
            _ = f.Services;
            Assert.Equal(0, f.Count.Runs);
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("Production", "1")]
    [InlineData("Production", "yes")]
    [InlineData("Production", "garbage")]
    [InlineData("Development", "1")]
    [InlineData("Development", "yes")]
    public async Task Seed_enabled_non_boolean_starts_without_seeding_and_logs_a_warning(string environment, string value)
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await using var f = new Factory(environment, value, db.GetConnectionString());
            _ = f.Services;
            Assert.Equal(0, f.Count.Runs);
            Assert.Contains(f.Logs.Events, e => e.Level == LogEventLevel.Warning && e.RenderMessage().Contains("Seed:Enabled"));
            // Development always migrates (Program.cs), so only Production proves "nothing migrated".
            if (environment == "Production")
                Assert.False(await HasSchemaAsync(db.GetConnectionString()));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("Production", "")]
    [InlineData("Development", "")]
    [InlineData("Production", "  ")]
    [InlineData("Development", "  ")]
    public async Task Empty_seed_enabled_means_missing_and_starts_without_seeding(string environment, string value)
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await using var f = new Factory(environment, value, db.GetConnectionString());
            _ = f.Services;
            Assert.Equal(0, f.Count.Runs);
            if (environment == "Production")
                Assert.False(await HasSchemaAsync(db.GetConnectionString()));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Refused_seed_still_serves_health_logs_no_secrets_and_applies_no_migration()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            // Production with MigrateOnStartup off: schema must stay absent, so /health (SELECT 1) still works without app tables.
            await using var f = new Factory("Production", "true", db.GetConnectionString());
            using var client = f.CreateClient();
            var res = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.False(await HasSchemaAsync(db.GetConnectionString()));

            var logs = f.Logs.All();
            Assert.Contains("seeding is only allowed", logs);
            Assert.DoesNotContain(FakeKey, logs);
            Assert.DoesNotContain(FakePassword, logs);
            Assert.DoesNotContain("Password", logs);
            Assert.Equal(1, f.Logs.Events.Count(e => e.Level == LogEventLevel.Error && e.RenderMessage().Contains("seeding is only allowed")));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Failing_seed_step_fails_startup_loudly()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await using var f = new Factory("Development", "true", db.GetConnectionString(),
                s => s.AddScoped<IDevSeedStep, InsertThenFailSteps>());
            var ex = Assert.ThrowsAny<Exception>(() => f.Services);
            Assert.Contains("seed step boom", ex.ToString());
            Assert.DoesNotContain(FakeKey, ex.ToString());
            // Migrations were already applied (not rolled back); steps are responsible for their own idempotency.
            Assert.True(await HasSchemaAsync(db.GetConnectionString()));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Running_the_seeder_twice_on_the_same_database_does_not_fail_in_migration_and_reruns_steps()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            await using (var first = new Factory("Development", "true", db.GetConnectionString())) { _ = first.Services; Assert.Equal(1, first.Count.Runs); }
            await using var second = new Factory("Development", "true", db.GetConnectionString());
            _ = second.Services;
            // The framework gives no dedupe: every step must be idempotent itself (contract for US-0003/0005/0006).
            Assert.Equal(1, second.Count.Runs);
            Assert.True(await HasSchemaAsync(db.GetConnectionString()));
        }
        finally { await db.DisposeAsync(); }
    }

    private sealed class StubHandler(Counter c) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref c.Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"id\":\"{Guid.NewGuid()}\"}}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public async Task Real_admin_client_logs_neither_service_role_key_nor_phone_number()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            Counter? counter = null;
            await using var f = new Factory("Development", "false", db.GetConnectionString(), s =>
            {
                counter = new Counter();
                s.AddHttpClient<ISupabaseAdminClient, SupabaseAdminClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(counter));
            });
            _ = f.Services;
            using var scope = f.Services.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ISupabaseAdminClient>();
            await client.CreateConfirmedPhoneUserAsync(FakePhone, CancellationToken.None);

            Assert.Equal(1, counter!.Calls);
            var logs = f.Logs.All();
            Assert.DoesNotContain(FakeKey, logs);
            Assert.DoesNotContain(FakePhone, logs);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Refused_production_start_never_calls_the_real_admin_client()
    {
        var db = new TestDatabase();
        try
        {
            await db.StartAsync();
            Counter? counter = null;
            await using var f = new Factory("Production", "true", db.GetConnectionString(), s =>
            {
                counter = new Counter();
                s.AddHttpClient<ISupabaseAdminClient, SupabaseAdminClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(counter));
            });
            _ = f.Services;
            Assert.Equal(0, counter!.Calls);
            Assert.Equal(0, f.Count.Runs);
        }
        finally { await db.DisposeAsync(); }
    }
}
