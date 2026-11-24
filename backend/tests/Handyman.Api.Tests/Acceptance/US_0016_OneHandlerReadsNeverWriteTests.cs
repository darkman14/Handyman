using Handyman.Api.Cqrs;
using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Cqrs;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

// QA-owned mutation-style checks for US-0016: do the architecture rules really bite, and does the read context really never write?

internal static class QaFakes
{
    public sealed record Q1 : IQuery<int>;
    internal sealed class ViaServiceProvider(IServiceProvider sp) : IQueryHandler<Q1, int>
    {
        public Task<int> Handle(Q1 q, CancellationToken ct) => Task.FromResult(sp.GetRequiredService<AppDbContext>().GetHashCode());
    }

    public sealed record Q2 : IQuery<int>;
    internal sealed class ViaLazy(Lazy<AppDbContext> db) : IQueryHandler<Q2, int>
    {
        public Task<int> Handle(Q2 q, CancellationToken ct) => Task.FromResult(db.Value.GetHashCode());
    }

    public sealed record Q3 : IQuery<int>;
    internal abstract class BaseWithApp(AppDbContext db) { public AppDbContext Db { get; } = db; }
    internal sealed class ViaBase(AppDbContext db) : BaseWithApp(db), IQueryHandler<Q3, int>
    {
        public Task<int> Handle(Q3 q, CancellationToken ct) => Task.FromResult(Db.GetHashCode());
    }

    public sealed record Q4 : IQuery<int>;
    internal sealed class ViaPrimaryCtorUsed(AppDbContext db) : IQueryHandler<Q4, int>
    {
        public Task<int> Handle(Q4 q, CancellationToken ct) => Task.FromResult(db.GetHashCode());
    }

    public sealed record Q5 : IQuery<int>;
    internal sealed class ViaProperty : IQueryHandler<Q5, int>
    {
        public AppDbContext? Db { get; init; }
        public Task<int> Handle(Q5 q, CancellationToken ct) => Task.FromResult(Db?.GetHashCode() ?? 0);
    }

    public sealed record Q6 : IQuery<int>;
    internal sealed class ViaBaseDbContext(DbContext db) : IQueryHandler<Q6, int>
    {
        public Task<int> Handle(Q6 q, CancellationToken ct) => Task.FromResult(db.GetHashCode());
    }

    public sealed record Q7 : IQuery<int>;
    internal sealed class ViaFactory(IDbContextFactory<AppDbContext> f) : IQueryHandler<Q7, int>
    {
        public Task<int> Handle(Q7 q, CancellationToken ct) => Task.FromResult(f.GetHashCode());
    }

    public sealed record C1 : ICommand<Unit>;
    internal abstract class CmdBase(ReadDbContext db) { public ReadDbContext Db { get; } = db; }
    internal sealed class CmdViaBase(ReadDbContext db) : CmdBase(db), ICommandHandler<C1, Unit>
    {
        public Task<Unit> Handle(C1 c, CancellationToken ct) => Task.FromResult(Unit.Value);
    }

    public sealed record C2 : ICommand<Unit>;
    internal sealed class CmdViaSp(IServiceProvider sp) : ICommandHandler<C2, Unit>
    {
        public Task<Unit> Handle(C2 c, CancellationToken ct) => Task.FromResult(sp.GetHashCode() == 0 ? Unit.Value : Unit.Value);
    }

    public sealed record C3 : ICommand<Unit>;
    public sealed record Q8 : IQuery<int>;
    internal sealed class Mixed(AppDbContext db) : ICommandHandler<C3, Unit>, IQueryHandler<Q8, int>
    {
        public Task<Unit> Handle(C3 c, CancellationToken ct) => Task.FromResult(Unit.Value);
        public Task<int> Handle(Q8 q, CancellationToken ct) => Task.FromResult(db.GetHashCode());
    }

    public sealed record Q9 : IQuery<int>;
    internal class Q9HandlerAParent : IQueryHandler<Q9, int> { public Task<int> Handle(Q9 q, CancellationToken ct) => Task.FromResult(2); }
    internal sealed class Q9HandlerB : Q9HandlerAParent { }
}

public class US_0016_MutationTests
{
    private static int Hits(params Type[] t) => ArchitectureRules.ContextViolations(t).Count;

    // Documented gaps: these pass today because the rule does not see them. Flip to 1 when the rule is hardened.
    [Fact] public void AC2_query_handler_with_IServiceProvider_is_NOT_caught_known_gap() => Hits(typeof(QaFakes.ViaServiceProvider)).ShouldBe(0);
    [Fact] public void AC2_query_handler_taking_plain_DbContext_is_NOT_caught_known_gap() => Hits(typeof(QaFakes.ViaBaseDbContext)).ShouldBe(0);
    [Fact] public void AC2_command_handler_with_IServiceProvider_is_NOT_caught_known_gap() => Hits(typeof(QaFakes.CmdViaSp)).ShouldBe(0);

    [Fact] public void AC2_query_handler_with_Lazy_AppDbContext_is_caught() => Hits(typeof(QaFakes.ViaLazy)).ShouldBe(1);
    [Fact] public void AC2_query_handler_inheriting_base_that_takes_AppDbContext_is_caught() => Hits(typeof(QaFakes.ViaBase)).ShouldBe(1);
    [Fact] public void AC2_query_handler_primary_constructor_is_caught() => Hits(typeof(QaFakes.ViaPrimaryCtorUsed)).ShouldBe(1);
    [Fact] public void AC2_query_handler_with_AppDbContext_property_is_caught() => Hits(typeof(QaFakes.ViaProperty)).ShouldBe(1);
    [Fact] public void AC2_query_handler_with_IDbContextFactory_of_AppDbContext_is_caught() => Hits(typeof(QaFakes.ViaFactory)).ShouldBe(1);
    [Fact] public void AC2_command_handler_inheriting_base_that_takes_ReadDbContext_is_caught() => Hits(typeof(QaFakes.CmdViaBase)).ShouldBe(1);
    [Fact] public void AC2_class_handling_both_sides_with_AppDbContext_is_reported_once() => Hits(typeof(QaFakes.Mixed)).ShouldBe(1);

    [Fact]
    public void AC1_two_handlers_one_via_inheritance_are_caught()
    {
        var v = ArchitectureRules.HandlerCountViolations([typeof(QaFakes.Q9), typeof(QaFakes.Q9HandlerAParent), typeof(QaFakes.Q9HandlerB)]);
        v.Count.ShouldBe(1);
        v[0].ShouldContain("2 handlers");
    }

    [Fact]
    public void AC1_request_without_handler_is_caught_and_internal_handler_satisfies_it()
    {
        ArchitectureRules.HandlerCountViolations([typeof(QaFakes.Q1)]).Count.ShouldBe(1);
        ArchitectureRules.HandlerCountViolations([typeof(QaFakes.Q1), typeof(QaFakes.ViaServiceProvider)]).ShouldBeEmpty();
    }
}

public class US_0016_ReadsNeverWriteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<string> Seed()
    {
        var name = "qa16-" + Guid.NewGuid().ToString("N");
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
    public async Task AC4_include_projection_fromsql_and_find_results_are_not_tracked()
    {
        var name = await Seed();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        var trade = await read.Trades.SingleAsync(t => t.Name == name);
        await read.Trades.Join(read.WorkerTrades, t => t.Id, w => w.TradeId, (t, w) => t).ToListAsync(); // no navigations in the model, so no Include
        await read.Trades.Where(t => t.Name == name).Select(t => new { t.Id, t.Name }).ToListAsync();
        await read.Trades.FromSqlRaw("select * from app.trades where name = {0}", name).ToListAsync();
        await read.Trades.FindAsync(trade.Id);

        read.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task AC4_removing_something_that_was_read_and_saving_never_writes()
    {
        var name = await Seed();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
        var trade = await read.Trades.SingleAsync(t => t.Name == name);
        read.Trades.Remove(trade);
        await Should.ThrowAsync<InvalidOperationException>(() => read.SaveChangesAsync());
        (await Count(name)).ShouldBe(1);
    }

    [Fact]
    public async Task AC4_reads_see_data_committed_by_other_contexts_with_auto_detect_off()
    {
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
        var name = "qa16-late-" + Guid.NewGuid().ToString("N");
        (await read.Trades.CountAsync(t => t.Name == name)).ShouldBe(0);
        using (var s2 = factory.Services.CreateScope())
        {
            var app = s2.ServiceProvider.GetRequiredService<AppDbContext>();
            app.Trades.Add(new Trade { Name = name });
            await app.SaveChangesAsync();
        }
        (await read.Trades.CountAsync(t => t.Name == name)).ShouldBe(1);
    }

    [Fact]
    public void AC4_read_context_uses_same_connection_and_schema_as_the_command_context()
    {
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
        var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        read.Database.GetConnectionString().ShouldBe(app.Database.GetConnectionString());
        read.Model.GetDefaultSchema().ShouldBe(app.Model.GetDefaultSchema());
    }

    // ---- bypasses of the SaveChanges guard (BUG-0002) ----

    [Fact]
    public async Task AC4_ExecuteUpdate_through_the_read_context_must_not_write()
    {
        var name = await Seed();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        try { await read.Trades.Where(t => t.Name == name).ExecuteUpdateAsync(s => s.SetProperty(t => t.Name, name + "-x")); }
        catch (InvalidOperationException) { }

        (await Count(name + "-x")).ShouldBe(0, "ExecuteUpdateAsync changed data through ReadDbContext");
    }

    [Fact]
    public async Task AC4_ExecuteDelete_through_the_read_context_must_not_write()
    {
        var name = await Seed();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        try { await read.Trades.Where(t => t.Name == name).ExecuteDeleteAsync(); } catch (InvalidOperationException) { }

        (await Count(name)).ShouldBe(1, "ExecuteDeleteAsync deleted data through ReadDbContext");
    }

    [Fact]
    public async Task AC4_ExecuteSql_through_the_read_context_must_not_write()
    {
        var name = await Seed();
        using var scope = factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        try { await read.Database.ExecuteSqlAsync($"delete from app.trades where name = {name}"); } catch (InvalidOperationException) { }

        (await Count(name)).ShouldBe(1, "ExecuteSql deleted data through ReadDbContext");
    }
}
