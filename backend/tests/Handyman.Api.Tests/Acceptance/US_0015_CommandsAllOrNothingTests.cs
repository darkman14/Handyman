using Handyman.Api.Cqrs;
using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Cqrs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Handyman.Api.Tests.Acceptance;

// QA-owned regression tests for US-0015 (beyond the dev's DecoratorTests).

public enum QaMode
{
    Ok, ThrowAfterTwoEntities, SecondContextFromScope, UnrelatedContextThenThrow, FailingCommit, DeepThrow, WaitThenThrow, WaitThenOk,
}

public sealed record QaCommand(string Prefix, QaMode Mode, int Pushes = 0, string PushType = "Hired",
    TaskCompletionSource? Started = null, Task? Gate = null) : ICommand<string>;

public sealed class QaHandler(AppDbContext db, IServiceProvider sp, DbContextOptions<AppDbContext> options, PendingPushes pushes)
    : ICommandHandler<QaCommand, string>
{
    public static readonly ApplicationException Boom = new("qa boom");
    public static bool SecondContextWasSameInstance;

    public async Task<string> Handle(QaCommand c, CancellationToken ct)
    {
        for (var i = 0; i < c.Pushes; i++) pushes.Add(Guid.NewGuid(), c.PushType + i, null);

        switch (c.Mode)
        {
            case QaMode.ThrowAfterTwoEntities:
                db.Trades.Add(new Trade { Name = c.Prefix + "-a" });
                await db.SaveChangesAsync(ct);
                db.Areas.Add(new Area { Name = c.Prefix + "-b", Kind = AreaKind.City });
                await db.SaveChangesAsync(ct);
                db.Trades.Add(new Trade { Name = c.Prefix + "-c" });
                await db.SaveChangesAsync(ct);
                throw Boom;
            case QaMode.SecondContextFromScope:
                var second = sp.GetRequiredService<AppDbContext>();
                SecondContextWasSameInstance = ReferenceEquals(second, db);
                db.Trades.Add(new Trade { Name = c.Prefix + "-1" });
                await db.SaveChangesAsync(ct);
                second.Trades.Add(new Trade { Name = c.Prefix + "-2" });
                await second.SaveChangesAsync(ct);
                throw Boom;
            case QaMode.UnrelatedContextThenThrow:
                await using (var other = new AppDbContext(options))
                {
                    other.Trades.Add(new Trade { Name = c.Prefix + "-other" });
                    await other.SaveChangesAsync(ct);
                }
                db.Trades.Add(new Trade { Name = c.Prefix + "-own" });
                await db.SaveChangesAsync(ct);
                throw Boom;
            case QaMode.FailingCommit:
                db.Trades.Add(new Trade { Name = c.Prefix + "-x" });
                await db.SaveChangesAsync(ct);
                // deferred FK: violation is only detected at COMMIT
                await db.Database.ExecuteSqlRawAsync("insert into app.qa_deferred(id, ref) values (1, 999)", ct);
                return "never";
            case QaMode.DeepThrow:
                db.Trades.Add(new Trade { Name = c.Prefix + "-d" });
                await db.SaveChangesAsync(ct);
                await ThrowDeep();
                return "never";
            case QaMode.WaitThenThrow:
                c.Started!.SetResult();
                await c.Gate!;
                throw Boom;
            case QaMode.WaitThenOk:
                db.Trades.Add(new Trade { Name = c.Prefix + "-ok" });
                await db.SaveChangesAsync(ct);
                return "ok";
            default:
                db.Trades.Add(new Trade { Name = c.Prefix + "-1" });
                db.Areas.Add(new Area { Name = c.Prefix + "-2", Kind = AreaKind.City });
                await db.SaveChangesAsync(ct);
                return "ok";
        }
    }

    private static async Task ThrowDeep()
    {
        await Task.Yield();
        throw Boom;
    }
}

public sealed class QaFixture : IAsyncLifetime
{
    private readonly TestDatabase _pg = new();
    public string ConnectionString => _pg.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            "create table app.qa_deferred(id int primary key, ref int references app.qa_deferred(id) deferrable initially deferred)");
    }

    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)).Options);
}

public sealed class RecordingQueue : IPushQueue
{
    public List<PushMessage> Received { get; } = [];
    public Func<PushMessage, bool>? ThrowWhen { get; set; }

    public void Enqueue(PushMessage message)
    {
        if (ThrowWhen?.Invoke(message) == true) throw new InvalidOperationException("queue exploded for +381601234567");
        lock (Received) Received.Add(message);
    }
}

public class US_0015_CommandsAllOrNothingTests(QaFixture fx) : IClassFixture<QaFixture>
{
    private sealed record H(ServiceProvider Root, TestLoggerProvider Logs, RecordingQueue Queue, string Prefix);

    private H NewHarness()
    {
        var logs = new TestLoggerProvider();
        var queue = new RecordingQueue();
        var services = new ServiceCollection();
        services.AddLogging(l => l.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(fx.ConnectionString,
            n => n.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)));
        services.AddSingleton<IPushQueue>(queue);
        services.AddCqrs(typeof(US_0015_CommandsAllOrNothingTests).Assembly);
        return new H(services.BuildServiceProvider(), logs, queue, "q" + Guid.NewGuid().ToString("N")[..12]);
    }

    private static Task<string> Run(H h, QaCommand cmd, CancellationToken ct = default)
    {
        var scope = h.Root.CreateScope();
        return RunIn(scope, cmd, ct, dispose: true);
    }

    private static async Task<string> RunIn(IServiceScope scope, QaCommand cmd, CancellationToken ct, bool dispose)
    {
        try { return await scope.ServiceProvider.GetRequiredService<ICommandHandler<QaCommand, string>>().Handle(cmd, ct); }
        finally { if (dispose) scope.Dispose(); }
    }

    private async Task<int> Rows(string prefix)
    {
        await using var db = fx.NewContext();
        return await db.Trades.CountAsync(t => t.Name.StartsWith(prefix)) + await db.Areas.CountAsync(a => a.Name.StartsWith(prefix));
    }

    // AC1
    [Fact]
    public async Task AC1_changes_on_several_entities_in_one_save_are_all_committed()
    {
        var h = NewHarness();
        (await Run(h, new QaCommand(h.Prefix, QaMode.Ok))).ShouldBe("ok");
        (await Rows(h.Prefix)).ShouldBe(2);
    }

    // AC2
    [Fact]
    public async Task AC2_rollback_removes_changes_of_several_entities_and_several_saves()
    {
        var h = NewHarness();
        var ex = await Should.ThrowAsync<ApplicationException>(() => Run(h, new QaCommand(h.Prefix, QaMode.ThrowAfterTwoEntities)));
        ex.ShouldBeSameAs(QaHandler.Boom);
        (await Rows(h.Prefix)).ShouldBe(0);
    }

    [Fact]
    public async Task AC2_second_context_resolved_from_the_same_scope_is_the_same_instance_and_is_rolled_back()
    {
        var h = NewHarness();
        await Should.ThrowAsync<ApplicationException>(() => Run(h, new QaCommand(h.Prefix, QaMode.SecondContextFromScope)));
        QaHandler.SecondContextWasSameInstance.ShouldBeTrue();
        (await Rows(h.Prefix)).ShouldBe(0);
    }

    // Documents a limit, not a defect: a context the handler creates itself is outside the transaction.
    [Fact]
    public async Task AC2_limit_a_hand_made_second_context_is_NOT_rolled_back()
    {
        var h = NewHarness();
        await Should.ThrowAsync<ApplicationException>(() => Run(h, new QaCommand(h.Prefix, QaMode.UnrelatedContextThenThrow)));
        var db = fx.NewContext();
        (await db.Trades.CountAsync(t => t.Name == h.Prefix + "-other")).ShouldBe(1); // leaked on purpose of this test
        (await db.Trades.CountAsync(t => t.Name == h.Prefix + "-own")).ShouldBe(0);
    }

    [Fact]
    public async Task AC2_original_exception_stack_trace_is_kept()
    {
        var h = NewHarness();
        var ex = await Should.ThrowAsync<ApplicationException>(() => Run(h, new QaCommand(h.Prefix, QaMode.DeepThrow)));
        ex.ShouldBeSameAs(QaHandler.Boom);
        ex.StackTrace!.ShouldContain("ThrowDeep");
        (await Rows(h.Prefix)).ShouldBe(0);
    }

    // AC3
    [Fact]
    public async Task AC3_pushes_arrive_in_queued_order_exactly_once_each()
    {
        var h = NewHarness();
        await Run(h, new QaCommand(h.Prefix, QaMode.Ok, Pushes: 3));
        h.Queue.Received.Select(p => p.Type).ShouldBe(["Hired0", "Hired1", "Hired2"]);
    }

    [Fact]
    public async Task AC3_a_queue_that_throws_after_commit_does_not_fail_the_command_and_the_rest_still_arrive()
    {
        var h = NewHarness();
        h.Queue.ThrowWhen = p => p.Type == "Hired1";
        (await Run(h, new QaCommand(h.Prefix, QaMode.Ok, Pushes: 3))).ShouldBe("ok");
        (await Rows(h.Prefix)).ShouldBe(2);
        h.Queue.Received.Select(p => p.Type).ShouldBe(["Hired0", "Hired2"]);
        var err = h.Logs.Entries.Single(e => e.Level == LogLevel.Error);
        err.Message.ShouldNotContain("381601234567");
        err.Message.ShouldNotContain("exploded");
        h.Logs.Entries.ShouldContain(e => e.Message.Contains("success")); // outcome still success
    }

    // AC4
    [Fact]
    public async Task AC4_a_commit_that_fails_in_the_database_drops_pushes_rolls_back_and_surfaces_the_database_error()
    {
        var h = NewHarness();
        var ex = await Should.ThrowAsync<Exception>(() => Run(h, new QaCommand(h.Prefix, QaMode.FailingCommit, Pushes: 2)));
        (ex is PostgresException || ex.InnerException is PostgresException).ShouldBeTrue(ex.GetType().Name);
        h.Queue.Received.ShouldBeEmpty();
        (await Rows(h.Prefix)).ShouldBe(0);
        h.Logs.Entries.ShouldContain(e => e.Message.Contains("failure"));
    }

    [Fact]
    public async Task AC4_an_already_cancelled_token_fails_before_anything_is_saved_or_pushed()
    {
        var h = NewHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => Run(h, new QaCommand(h.Prefix, QaMode.Ok, Pushes: 1), cts.Token));
        h.Queue.Received.ShouldBeEmpty();
        (await Rows(h.Prefix)).ShouldBe(0);
    }

    // PendingPushes scoping
    [Fact]
    public async Task Pushes_of_a_failed_command_do_not_leak_into_the_next_command_in_the_same_scope()
    {
        var h = NewHarness();
        using var scope = h.Root.CreateScope();
        await Should.ThrowAsync<ApplicationException>(() =>
            RunIn(scope, new QaCommand(h.Prefix, QaMode.ThrowAfterTwoEntities, Pushes: 2, PushType: "Failed"), default, false));
        await RunIn(scope, new QaCommand(h.Prefix, QaMode.WaitThenOk, Pushes: 1, PushType: "Good"), default, false);
        h.Queue.Received.Select(p => p.Type).ShouldBe(["Good0"]);
    }

    [Fact]
    public async Task Pushes_of_a_succeeded_command_are_not_resent_by_the_next_command_in_the_same_scope()
    {
        var h = NewHarness();
        using var scope = h.Root.CreateScope();
        await RunIn(scope, new QaCommand(h.Prefix, QaMode.Ok, Pushes: 1, PushType: "First"), default, false);
        await RunIn(scope, new QaCommand(h.Prefix + "b", QaMode.Ok, Pushes: 0), default, false);
        h.Queue.Received.Select(p => p.Type).ShouldBe(["First0"]);
    }

    [Fact]
    public async Task Parallel_scopes_do_not_share_pending_pushes()
    {
        var h = NewHarness();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // A queues 2 pushes, waits, then fails. B runs and commits meanwhile.
        var a = Run(h, new QaCommand(h.Prefix + "A", QaMode.WaitThenThrow, Pushes: 2, PushType: "A", Started: started, Gate: gate.Task));
        await started.Task;
        await Run(h, new QaCommand(h.Prefix + "B", QaMode.WaitThenOk, Pushes: 1, PushType: "B"));
        gate.SetResult();
        await Should.ThrowAsync<ApplicationException>(() => a);

        h.Queue.Received.Select(p => p.Type).ShouldBe(["B0"]);
    }

    // AC5 / registration
    [Fact]
    public async Task AC5_logs_hold_use_case_name_not_the_command_record_and_no_exception_message()
    {
        var h = NewHarness();
        await Should.ThrowAsync<ApplicationException>(() => Run(h, new QaCommand("+381601234567 Knez Mihailova 1", QaMode.DeepThrow)));
        h.Logs.Entries.ShouldAllBe(e => !e.Message.Contains("381601234567") && !e.Message.Contains("qa boom") && !e.Message.Contains("Knez"));
        h.Logs.Entries.ShouldContain(e => e.Message.Contains("QaCommand") && e.Message.Contains("failure") && e.Message.Contains(" ms"));
    }

    [Fact]
    public void Registration_has_no_duplicate_handler_registrations_and_a_missing_handler_fails_clearly()
    {
        var h = NewHarness();
        using var scope = h.Root.CreateScope();
        scope.ServiceProvider.GetServices<ICommandHandler<QaCommand, string>>().Count().ShouldBe(1);
        var ex = Should.Throw<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<ICommandHandler<MissingCommand, string>>());
        ex.Message.ShouldContain("MissingCommand");
    }

    private sealed record MissingCommand : ICommand<string>;
}
