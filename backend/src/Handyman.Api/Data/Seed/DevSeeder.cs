using Microsoft.EntityFrameworkCore;
using Handyman.Api.Infrastructure;

namespace Handyman.Api.Data.Seed;

/// <summary>One piece of development data (users, sample jobs, ...). Registered as scoped; run by <see cref="DevSeeder"/>.</summary>
public interface IDevSeedStep
{
    Task SeedAsync(CancellationToken ct);
}

/// <summary>Runs every registered <see cref="IDevSeedStep"/>. Reached only through <see cref="DevSeedStartup"/>.</summary>
public class DevSeeder(IEnumerable<IDevSeedStep> steps)
{
    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var step in steps)
            await step.SeedAsync(ct);
    }
}

/// <summary>Decides at startup whether seeding is allowed, and if so migrates first and then seeds.</summary>
public static class DevSeedStartup
{
    /// <summary>Exactly Development or Test (any casing). Production, Staging and unknown names are refused.</summary>
    public static bool IsAllowedEnvironment(IHostEnvironment env) =>
        string.Equals(env.EnvironmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
        || string.Equals(env.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true when the seed ran.</summary>
    public static async Task<bool> RunAsync(IServiceProvider services, IHostEnvironment env, IConfiguration config, CancellationToken ct = default)
    {
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevSeeder));

        // Tolerant parse: an empty or non-boolean value must never crash startup and never seed.
        var raw = config["Seed:Enabled"]?.Trim();
        if (!bool.TryParse(raw, out var enabled))
        {
            if (!string.IsNullOrEmpty(raw))
                log.LogWarning("Seed:Enabled has a value that is not a boolean and was ignored; nothing was seeded.");
            return false;
        }
        if (!enabled)
            return false;

        if (!IsAllowedEnvironment(env))
        {
            log.LogError("Seed:Enabled is true but the environment is {Environment}; seeding is only allowed in Development and Test. Nothing was seeded.",
                env.EnvironmentName);
            return false;
        }

        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(ct);
        await scope.ServiceProvider.GetRequiredService<DevSeeder>().RunAsync(ct);
        log.LogInformation("Development data seeded.");
        return true;
    }
}
