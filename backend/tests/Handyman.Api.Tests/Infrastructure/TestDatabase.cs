global using Handyman.Api.Tests.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Handyman.Api.Tests.Infrastructure;

/// <summary>
/// One Postgres container per test run, shared by every fixture (BUG-0009). Each fixture gets its own empty database
/// from <see cref="TestDatabase"/>, so tests stay isolated without starting a container per class.
/// </summary>
internal static class SharedPostgres
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(StartAsync);
    private static int _counter;

    static SharedPostgres()
    {
        // Ryuk removes the container if the process dies; this stops it promptly on a normal exit.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Container.IsValueCreated && Container.Value.IsCompletedSuccessfully)
                Container.Value.Result.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(20));
        };
    }

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        // Single start, no parallel competition for Docker. Tests run in parallel across classes and open many
        // connections, so lift max_connections; fsync is irrelevant for throwaway data.
        var container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithCommand("-c", "max_connections=1000", "-c", "fsync=off", "-c", "synchronous_commit=off")
            .Build();
        await container.StartAsync();
        return container;
    }

    public static async Task<string> CreateDatabaseAsync()
    {
        var container = await Container.Value;
        var admin = container.GetConnectionString();
        var name = $"t{Interlocked.Increment(ref _counter)}_{Guid.NewGuid():N}";
        await using (var conn = new NpgsqlConnection(admin))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
    }

    public static async Task DropDatabaseAsync(string connectionString)
    {
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false }.ConnectionString;
        await using var conn = new NpgsqlConnection(admin);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Connection string with the shared server's default (admin) database, for tests that make their own databases.</summary>
    public static async Task<string> AdminConnectionStringAsync() => (await Container.Value).GetConnectionString();
}

/// <summary>Drop-in for the former per-fixture container: StartAsync creates an empty database, DisposeAsync drops it.</summary>
internal sealed class TestDatabase
{
    private string? _connectionString;

    public string GetConnectionString() => _connectionString ?? throw new InvalidOperationException("Call StartAsync first.");

    public async Task StartAsync() => _connectionString = await SharedPostgres.CreateDatabaseAsync();

    public async ValueTask DisposeAsync()
    {
        if (_connectionString is null) return;
        try { await SharedPostgres.DropDatabaseAsync(_connectionString); }
        catch (Exception) { /* the container is thrown away at the end of the run anyway */ }
    }
}
