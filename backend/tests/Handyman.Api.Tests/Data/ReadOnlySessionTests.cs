using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Handyman.Api.Tests.Data;

public class ReadOnlySessionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<string> Seed()
    {
        var name = "ro-" + Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Trades.Add(new Trade { Name = name });
        await db.SaveChangesAsync();
        return name;
    }

    private async Task<int> Count(string name)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Trades.CountAsync(t => t.Name == name);
    }

    [Fact]
    public async Task The_database_session_of_the_read_context_refuses_writes_even_without_ef()
    {
        var name = await Seed();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
        var connection = read.Database.GetDbConnection();
        await read.Database.OpenConnectionAsync();

        await using var command = connection.CreateCommand(); // plain ADO.NET: no EF command interceptor involved
        command.CommandText = "delete from app.trades where name = '" + name + "'";
        await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        (await Count(name)).ShouldBe(1);
    }

    [Fact]
    public async Task The_read_only_session_is_applied_again_after_the_connection_returns_to_the_pool()
    {
        var name = await Seed();
        for (var i = 0; i < 3; i++)
        {
            using var scope = factory.Services.CreateScope();
            var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
            await read.Database.OpenConnectionAsync();
            await using var command = read.Database.GetDbConnection().CreateCommand();
            command.CommandText = "delete from app.trades where name = '" + name + "'";
            await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }
        (await Count(name)).ShouldBe(1);
    }

    [Fact]
    public async Task FromSql_with_a_data_modifying_statement_is_refused()
    {
        var name = await Seed();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            read.Trades.FromSqlRaw("delete from app.trades where name = {0} returning *", name).ToListAsync());

        (await Count(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Selects_through_FromSql_and_ExecuteSql_free_queries_still_work()
    {
        var name = await Seed();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        (await read.Trades.FromSqlRaw("select * from app.trades where name = {0}", name).CountAsync()).ShouldBe(1);
        (await read.Trades.FromSqlRaw("-- tag\nwith t as (select * from app.trades) select * from t where name = {0}", name).CountAsync()).ShouldBe(1);
        (await read.Trades.CountAsync(t => t.Name == name)).ShouldBe(1);
    }
}
