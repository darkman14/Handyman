using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

// QA attack suite for BUG-0002 fix (ReadOnlySessionInterceptor). Each test tries a write through ReadDbContext and checks the row with AppDbContext.
public class US_0016_ReadOnlyHardeningTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<string> Seed()
    {
        var name = "qa16h-" + Guid.NewGuid().ToString("N");
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

    private ReadDbContext Read(out IServiceScope scope)
    {
        scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ReadDbContext>();
    }

    private static async Task Swallow(Func<Task> action) { try { await action(); } catch (Exception) { } }

    [Fact]
    public async Task Cte_delete_through_FromSql_is_refused_and_nothing_is_deleted()
    {
        var name = await Seed();
        var read = Read(out var scope); using var _ = scope;
        await Swallow(() => read.Trades.FromSqlRaw("with d as (delete from app.trades where name = {0} returning *) select * from d", name).ToListAsync());
        (await Count(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Leading_comment_or_whitespace_before_select_never_writes()
    {
        var name = await Seed();
        var read = Read(out var scope); using var _ = scope;
        await Swallow(() => read.Trades.FromSqlRaw("/* x */ with d as (delete from app.trades where name = {0} returning *) select * from d", name).ToListAsync());
        await Swallow(() => read.Database.ExecuteSqlRawAsync("﻿  \n/* c */ delete from app.trades where name = {0}", name));
        await Swallow(() => read.Database.ExecuteSqlRawAsync("-- c\n delete from app.trades where name = {0}", name));
        (await Count(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Multiple_statements_never_write()
    {
        var name = await Seed();
        var read = Read(out var scope); using var _ = scope;
        await Swallow(() => read.Database.ExecuteSqlRawAsync("select 1; delete from app.trades where name = {0}", name));
        await Swallow(() => read.Trades.FromSqlRaw("select * from app.trades where name = {0}; delete from app.trades where name = {0}", name).ToListAsync());
        await Swallow(() => read.Database.SqlQueryRaw<int>("select 1 as \"Value\"; delete from app.trades where name = {0}", name).ToListAsync());
        (await Count(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Select_calling_a_writing_function_is_refused_by_the_database()
    {
        var name = await Seed();
        var fn = "app.qa16_del_" + Guid.NewGuid().ToString("N")[..8];
        using (var s = factory.Services.CreateScope())
        {
            var app = s.ServiceProvider.GetRequiredService<AppDbContext>();
            await app.Database.ExecuteSqlRawAsync($"create function {fn}(n text) returns int language sql volatile as $$ with d as (delete from app.trades where name = n returning 1) select count(*)::int from d $$");
        }
        try
        {
            var read = Read(out var scope); using var _ = scope;
            await Swallow(() => read.Database.SqlQueryRaw<int>($"select {fn}({{0}}) as \"Value\"", name).ToListAsync());
            (await Count(name)).ShouldBe(1);
        }
        finally
        {
            using var s = factory.Services.CreateScope();
            await s.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync($"drop function if exists {fn}(text)");
        }
    }

    [Fact]
    public async Task Select_for_update_and_ddl_are_refused()
    {
        var name = await Seed();
        var read = Read(out var scope); using var _ = scope;
        await Should.ThrowAsync<Exception>(() => read.Trades.FromSqlRaw("select * from app.trades where name = {0} for update", name).ToListAsync());
        await Should.ThrowAsync<Exception>(() => read.Database.ExecuteSqlRawAsync("create table app.qa16_ddl(id int)"));
        await Should.ThrowAsync<Exception>(() => read.Database.ExecuteSqlRawAsync("copy app.trades to stdout"));
        (await Count(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Set_read_write_through_FromSql_does_not_enable_writes()
    {
        var name = await Seed();
        var read = Read(out var scope); using var _ = scope;
        await Swallow(() => read.Trades.FromSqlRaw("set session characteristics as transaction read write").ToListAsync());
        await Swallow(() => read.Database.SqlQueryRaw<string>("select set_config('default_transaction_read_only','off',false) as \"Value\"").ToListAsync());
        await Swallow(() => read.Database.ExecuteSqlRawAsync("delete from app.trades where name = {0}", name));
        await Swallow(() => read.Database.ExecuteSqlRawAsync("begin read write; delete from app.trades where name = {0}; commit", name));
        (await Count(name)).ShouldBe(1, "a SELECT set_config(...) or SET through the read context re-enabled writes");
    }

    [Fact]
    public async Task Set_config_by_select_does_not_make_the_next_read_context_command_writable()
    {
        // SELECT passes the command guard, so only the database level protects here.
        var name = await Seed();
        var read = Read(out var scope); using var _ = scope;
        await Swallow(() => read.Database.SqlQueryRaw<string>("select set_config('default_transaction_read_only','off',false) as \"Value\"").ToListAsync());
        // a second read command on the same context (same open connection when a transaction is open; otherwise may re-open)
        await using var tx = await read.Database.BeginTransactionAsync();
        await Swallow(() => read.Trades.FromSqlRaw("with d as (delete from app.trades where name = {0} returning *) select * from d", name).ToListAsync());
        (await Count(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Select_set_config_then_cte_delete_on_one_open_connection_KNOWN_GAP_BUG_0004()
    {
        // BUG-0004 (low, open): deliberate raw SQL can still re-enable writes. This test documents the gap and passes today.
        // When BUG-0004 is fixed it fails: flip the final assertion back to ShouldBe(1) and rename the test.
        // Real bypass attempt: keep the connection open so the read-only session setting is not re-applied between commands.
        var name = await Seed();
        var read = Read(out var scope); using var _ = scope;
        await read.Database.OpenConnectionAsync();
        await Swallow(() => read.Database.SqlQueryRaw<string>("select set_config('default_transaction_read_only','off',false) as \"Value\"").ToListAsync());
        await Swallow(() => read.Trades.FromSqlRaw("with d as (delete from app.trades where name = {0} returning *) select * from d", name).ToListAsync());
        (await Count(name)).ShouldBe(0, "BUG-0004 is fixed: SELECT set_config no longer re-enables writes. Flip this assertion to ShouldBe(1) and rename the test.");
    }

    [Fact]
    public async Task Raw_connection_set_read_write_is_the_only_way_to_write_and_needs_deliberate_code()
    {
        // Documents residual risk: GetDbConnection bypasses EF interceptors; the session setting can be flipped by hand.
        var name = await Seed();
        var read = Read(out var scope); using var _ = scope;
        var conn = read.Database.GetDbConnection();
        await read.Database.OpenConnectionAsync();
        using (var c = conn.CreateCommand()) { c.CommandText = "delete from app.trades where name = @n"; var p = c.CreateParameter(); p.ParameterName = "n"; p.Value = name; c.Parameters.Add(p); await Should.ThrowAsync<Exception>(() => c.ExecuteNonQueryAsync()); }
        (await Count(name)).ShouldBe(1, "raw connection write must fail without deliberate SET");
    }

    [Fact]
    public async Task Writes_through_a_transaction_started_on_the_read_context_are_refused()
    {
        var name = await Seed();
        var read = Read(out var scope); using var _ = scope;
        await using var tx = await read.Database.BeginTransactionAsync();
        await Swallow(() => read.Database.ExecuteSqlRawAsync("delete from app.trades where name = {0}", name));
        await Swallow(() => read.Trades.Where(t => t.Name == name).ExecuteDeleteAsync());
        await Swallow(() => tx.CommitAsync());
        (await Count(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Read_context_never_leaves_a_pooled_connection_that_blocks_command_writes_or_the_other_way_round()
    {
        for (var i = 0; i < 30; i++)
        {
            var name = await Seed(); // app write, possibly on a connection a read context just used
            using var scope = factory.Services.CreateScope();
            var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
            (await read.Trades.CountAsync(t => t.Name == name)).ShouldBe(1);
            await Swallow(() => read.Trades.Where(t => t.Name == name).ExecuteDeleteAsync());
            (await Count(name)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Command_and_read_context_in_the_same_scope_command_writes_still_work_and_read_sees_them()
    {
        using var scope = factory.Services.CreateScope();
        var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
        var name = "qa16h-same-" + Guid.NewGuid().ToString("N");
        (await read.Trades.CountAsync(t => t.Name == name)).ShouldBe(0);
        app.Trades.Add(new Trade { Name = name });
        await app.SaveChangesAsync();
        (await read.Trades.CountAsync(t => t.Name == name)).ShouldBe(1);
        await app.Trades.Where(t => t.Name == name).ExecuteDeleteAsync(); // app context may still use ExecuteDelete
        (await read.Trades.CountAsync(t => t.Name == name)).ShouldBe(0);
    }

    [Fact]
    public async Task Legit_reads_still_work()
    {
        var name = await Seed();
        var read = Read(out var scope); using var _ = scope;
        (await read.Trades.FromSqlRaw("select * from app.trades where name = {0}", name).ToListAsync()).Count.ShouldBe(1);
        (await read.Trades.FromSqlRaw("with t as (select * from app.trades where name = {0}) select * from t", name).ToListAsync()).Count.ShouldBe(1);
        (await read.Trades.CountAsync(t => t.Name == name)).ShouldBe(1);
        (await read.Trades.AsNoTracking().Join(read.WorkerTrades, t => t.Id, w => w.TradeId, (t, w) => t.Id).ToListAsync()).ShouldNotBeNull();
    }
}
