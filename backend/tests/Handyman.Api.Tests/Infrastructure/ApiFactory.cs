using Microsoft.Extensions.Configuration;
using Handyman.Api.Data;
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
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

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
        }));

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = TestAuth.Issuer,
                    ValidAudience = TestAuth.Audience,
                    IssuerSigningKey = TestAuth.Key,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                };
            });
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

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
