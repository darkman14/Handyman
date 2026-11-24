using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Handyman.Api.Tests.Cqrs;

public class ReadDbContextTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<string> SeedTrade()
    {
        var name = "read-" + Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Trades.Add(new Trade { Name = name });
        await db.SaveChangesAsync();
        return name;
    }

    private async Task<int> CountTrades(string name)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Trades.CountAsync(t => t.Name == name);
    }

    // AC4
    [Fact]
    public async Task Saving_through_the_read_context_fails_synchronously_and_asynchronously_and_writes_nothing()
    {
        var name = "never-" + Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
        read.Trades.Add(new Trade { Name = name });

        Should.Throw<InvalidOperationException>(() => read.SaveChanges());
        Should.Throw<InvalidOperationException>(() => read.SaveChanges(acceptAllChangesOnSuccess: false));
        await Should.ThrowAsync<InvalidOperationException>(() => read.SaveChangesAsync());
        await Should.ThrowAsync<InvalidOperationException>(() => read.SaveChangesAsync(true, CancellationToken.None));

        (await CountTrades(name)).ShouldBe(0);
    }

    // AC4
    [Fact]
    public async Task Changing_something_that_was_read_and_saving_leaves_the_database_unchanged()
    {
        var name = await SeedTrade();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        var trade = await read.Trades.SingleAsync(t => t.Name == name);
        trade.Name = "changed-" + name;

        await Should.ThrowAsync<InvalidOperationException>(() => read.SaveChangesAsync());
        (await CountTrades(name)).ShouldBe(1);
        (await CountTrades("changed-" + name)).ShouldBe(0);
    }

    // AC4
    [Fact]
    public async Task Data_read_through_the_read_context_is_not_tracked()
    {
        var name = await SeedTrade();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        var trade = await read.Trades.SingleAsync(t => t.Name == name);

        trade.Name.ShouldBe(name);
        read.ChangeTracker.QueryTrackingBehavior.ShouldBe(QueryTrackingBehavior.NoTracking);
        read.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_read_context_sees_the_same_tables_as_the_command_context()
    {
        var name = await SeedTrade();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        (await read.Trades.CountAsync(t => t.Name == name)).ShouldBe(1);
        read.Model.GetEntityTypes().Count().ShouldBe(
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Model.GetEntityTypes().Count());
    }
}
