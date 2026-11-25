using Microsoft.Extensions.Configuration;
using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Handyman.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Handyman.Api.Tests.Infrastructure;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly TestDatabase _postgres = new();

    public FakeBlobStorage Blobs { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Supabase:Url", "http://test.local");

        // appsettings.Local.json is added after host settings (Test counts as local), so UseSetting alone loses
        // against a developer's local connection string and the tests would write into the dev database.
        // ConfigureAppConfiguration is applied last and wins.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
            ["Supabase:Url"] = "http://test.local",
            // Production validation (issuer, audience, algorithms, skew, sub) runs against the test key.
            ["Supabase:JwtSecret"] = TestAuth.Secret,
        }));

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TestErrorCalls>();
            services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, TestErrorEndpointsStartupFilter>();
            services.RemoveAll<IBlobStorage>();
            services.AddSingleton<IBlobStorage>(Blobs);
        });
    }

    public HttpClient ClientFor(Guid userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestAuth.TokenFor(userId));
        return client;
    }

    /// <summary>Inserts an app.users row (the API needs one for every signed-in caller).</summary>
    public async Task<Guid> SeedUserAsync(Guid? id = null, UserRole role = UserRole.Client, DateTimeOffset? blockedAt = null)
    {
        var user = new User
        {
            Id = id ?? Guid.NewGuid(),
            Phone = "+38169" + Random.Shared.NextInt64(1_000_000, 9_999_999),
            Role = role,
            Name = "Test User",
            BlockedAt = blockedAt,
        };
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Seeds a user and returns a client signed in as that user.</summary>
    public async Task<(Guid Id, HttpClient Client)> SignedInUserAsync(UserRole role = UserRole.Client, DateTimeOffset? blockedAt = null)
    {
        var id = await SeedUserAsync(role: role, blockedAt: blockedAt);
        return (id, ClientFor(id));
    }

    /// <summary>Sets or clears blocked_at the way an admin would.</summary>
    public async Task SetBlockedAtAsync(Guid userId, DateTimeOffset? blockedAt)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.BlockedAt, blockedAt));
    }

    public async Task DeleteUserAsync(Guid userId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
