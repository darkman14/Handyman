using Handyman.Api.Cqrs;
using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Handyman.Api.Tests.Cqrs;

// ---- test-only use cases (found by AddCqrs because it scans this assembly) ----

public sealed record SaveTradesCommand(
    string Prefix, int Count, int PushCount = 0, bool Throw = false, bool BadSecondSave = false,
    CancellationTokenSource? CancelDuringHandler = null, bool ThrowAfterCancel = false,
    string Secret = "") : ICommand<string>;

public sealed class SaveTradesHandler(AppDbContext db, PendingPushes pushes) : ICommandHandler<SaveTradesCommand, string>
{
    public static readonly InvalidOperationException Boom = new("handler failed");

    public async Task<string> Handle(SaveTradesCommand c, CancellationToken ct)
    {
        for (var i = 0; i < c.Count; i++)
        {
            db.Trades.Add(new Trade { Name = $"{c.Prefix}-{i}" });
            await db.SaveChangesAsync(ct);
        }
        for (var i = 0; i < c.PushCount; i++) pushes.Add(Guid.Parse("00000000-0000-0000-0000-000000000001"), "Hired", null);
        if (c.BadSecondSave)
        {
            db.Trades.Add(new Trade { Name = new string('x', 500) }); // too long: the save fails
            await db.SaveChangesAsync(ct);
        }
        if (c.Throw) throw Boom;
        if (c.CancelDuringHandler is { } cts)
        {
            cts.Cancel();
            if (c.ThrowAfterCancel) ct.ThrowIfCancellationRequested();
        }
        return $"saved {c.Count}";
    }
}

public sealed record SlowQuery(int Milliseconds, bool Throw = false) : IQuery<int>;

public sealed class SlowQueryHandler : IQueryHandler<SlowQuery, int>
{
    public async Task<int> Handle(SlowQuery q, CancellationToken ct)
    {
        await Task.Delay(q.Milliseconds, ct);
        if (q.Throw) throw new InvalidOperationException("query failed");
        return 42;
    }
}

// ---- test helpers ----

public sealed record LogEntry(string Category, LogLevel Level, string Message);

public sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries { get { lock (_entries) return _entries.ToArray(); } }

    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, this);

    public void Dispose() { }

    private sealed class TestLogger(string category, TestLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (owner._entries) owner._entries.Add(new LogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>Records pushes and how many rows were already visible to a separate connection when each arrived.</summary>
public sealed class FakePushQueue(Func<int> committedRows) : IPushQueue
{
    public List<PushMessage> Received { get; } = [];
    public List<int> RowsVisibleAtEnqueue { get; } = [];

    public void Enqueue(PushMessage message)
    {
        Received.Add(message);
        RowsVisibleAtEnqueue.Add(committedRows());
    }
}

public class DecoratorFixture : IAsyncLifetime
{
    private readonly TestDatabase _postgres = new();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema))
        .Options);
}

public class DecoratorTests(DecoratorFixture fixture) : IClassFixture<DecoratorFixture>
{
    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Root { get; init; }
        public required TestLoggerProvider Logs { get; init; }
        public required FakePushQueue Queue { get; init; }
        public required string Prefix { get; init; }
        public required DecoratorFixture Fixture { get; init; }

        public IServiceScope NewScope() => Root.CreateScope();

        public async Task<int> RowCount()
        {
            await using var db = Fixture.NewContext();
            return await db.Trades.CountAsync(t => t.Name.StartsWith(Prefix));
        }

        public ValueTask DisposeAsync() => Root.DisposeAsync();
    }

    private Harness NewHarness()
    {
        var prefix = "t" + Guid.NewGuid().ToString("N")[..12];
        var logs = new TestLoggerProvider();
        var queue = new FakePushQueue(() =>
        {
            using var db = fixture.NewContext();
            return db.Trades.Count(t => t.Name.StartsWith(prefix));
        });

        var services = new ServiceCollection();
        services.AddLogging(l => l.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(fixture.ConnectionString,
            n => n.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)));
        services.AddSingleton<IPushQueue>(queue);
        services.AddCqrs(typeof(DecoratorTests).Assembly);

        return new Harness
        {
            Root = services.BuildServiceProvider(), Logs = logs, Queue = queue, Prefix = prefix, Fixture = fixture,
        };
    }

    private static async Task<string> Run(Harness h, SaveTradesCommand cmd, CancellationToken ct = default)
    {
        using var scope = h.NewScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<SaveTradesCommand, string>>().Handle(cmd, ct);
    }

    // AC1
    [Fact]
    public async Task Success_commits_every_change_and_returns_the_handler_result_unchanged()
    {
        await using var h = NewHarness();

        var result = await Run(h, new SaveTradesCommand(h.Prefix, 3));

        result.ShouldBe("saved 3");
        (await h.RowCount()).ShouldBe(3);
    }

    // AC2
    [Fact]
    public async Task Handler_exception_rolls_back_all_changes_and_the_same_exception_reaches_the_caller()
    {
        await using var h = NewHarness();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => Run(h, new SaveTradesCommand(h.Prefix, 2, Throw: true)));

        thrown.ShouldBeSameAs(SaveTradesHandler.Boom);
        (await h.RowCount()).ShouldBe(0);
    }

    // AC3
    [Fact]
    public async Task Pushes_reach_the_queue_once_each_and_only_after_the_commit()
    {
        await using var h = NewHarness();

        await Run(h, new SaveTradesCommand(h.Prefix, 2, PushCount: 2));

        h.Queue.Received.Count.ShouldBe(2);
        h.Queue.Received.ShouldAllBe(p => p.Type == "Hired");
        h.Queue.RowsVisibleAtEnqueue.ShouldAllBe(rows => rows == 2); // already committed and visible elsewhere
    }

    [Fact]
    public async Task Success_without_pushes_hands_nothing_to_the_queue()
    {
        await using var h = NewHarness();

        await Run(h, new SaveTradesCommand(h.Prefix, 1));

        h.Queue.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_push_queued_twice_is_handed_over_twice()
    {
        await using var h = NewHarness();

        await Run(h, new SaveTradesCommand(h.Prefix, 1, PushCount: 2));

        h.Queue.Received.Count.ShouldBe(2);
        h.Queue.Received[0].ShouldBe(h.Queue.Received[1]);
    }

    // AC4
    [Fact]
    public async Task Pushes_are_dropped_when_the_handler_throws()
    {
        await using var h = NewHarness();

        await Should.ThrowAsync<InvalidOperationException>(
            () => Run(h, new SaveTradesCommand(h.Prefix, 1, PushCount: 3, Throw: true)));

        h.Queue.Received.ShouldBeEmpty();
        (await h.RowCount()).ShouldBe(0);
    }

    [Fact]
    public async Task Pushes_are_dropped_when_a_save_fails_after_they_were_queued()
    {
        await using var h = NewHarness();

        await Should.ThrowAsync<DbUpdateException>(
            () => Run(h, new SaveTradesCommand(h.Prefix, 1, PushCount: 2, BadSecondSave: true)));

        h.Queue.Received.ShouldBeEmpty();
        (await h.RowCount()).ShouldBe(0);
    }

    // cancellation behaves like failure
    [Fact]
    public async Task Cancellation_inside_the_handler_rolls_back_and_drops_pushes()
    {
        await using var h = NewHarness();
        using var cts = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(() => Run(h,
            new SaveTradesCommand(h.Prefix, 2, PushCount: 1, CancelDuringHandler: cts, ThrowAfterCancel: true), cts.Token));

        h.Queue.Received.ShouldBeEmpty();
        (await h.RowCount()).ShouldBe(0);
        h.Logs.Entries.ShouldContain(e => e.Message.Contains("failure") && e.Message.Contains("SaveTrades"));
    }

    [Fact]
    public async Task Cancellation_before_commit_does_not_commit_or_push()
    {
        await using var h = NewHarness();
        using var cts = new CancellationTokenSource();

        // the handler cancels and still returns normally; the commit must not go through
        await Should.ThrowAsync<OperationCanceledException>(() => Run(h,
            new SaveTradesCommand(h.Prefix, 2, PushCount: 1, CancelDuringHandler: cts), cts.Token));

        h.Queue.Received.ShouldBeEmpty();
        (await h.RowCount()).ShouldBe(0);
    }

    // AC5
    [Fact]
    public async Task Command_logs_use_case_name_duration_and_success()
    {
        await using var h = NewHarness();

        await Run(h, new SaveTradesCommand(h.Prefix, 1));

        var entry = h.Logs.Entries.Single(e => e.Category.Contains("LoggingCommandDecorator"));
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain("SaveTradesCommand");
        entry.Message.ShouldContain("success");
        entry.Message.ShouldContain(" ms");
    }

    [Fact]
    public async Task Command_logs_failure_without_the_exception_message()
    {
        await using var h = NewHarness();

        await Should.ThrowAsync<InvalidOperationException>(
            () => Run(h, new SaveTradesCommand(h.Prefix, 1, Throw: true)));

        var entry = h.Logs.Entries.Single(e => e.Category.Contains("LoggingCommandDecorator"));
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("failure");
        entry.Message.ShouldContain("InvalidOperationException");
        entry.Message.ShouldNotContain("handler failed");
    }

    [Fact]
    public async Task Logs_never_contain_request_content_such_as_phone_numbers_or_addresses()
    {
        await using var h = NewHarness();
        var cmd = new SaveTradesCommand(h.Prefix, 1, Secret: "+381601234567 Knez Mihailova 1");

        await Run(h, cmd);

        h.Logs.Entries.ShouldAllBe(e => !e.Message.Contains("381601234567") && !e.Message.Contains("Knez Mihailova"));
    }

    [Fact]
    public async Task Fast_query_logs_information_with_name_and_success()
    {
        await using var h = NewHarness();
        using var scope = h.NewScope();

        var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<SlowQuery, int>>()
            .Handle(new SlowQuery(0), CancellationToken.None);

        result.ShouldBe(42);
        var entry = h.Logs.Entries.Single(e => e.Category.Contains("LoggingQueryDecorator"));
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain("SlowQuery");
        entry.Message.ShouldContain("success");
    }

    [Fact]
    public async Task Query_slower_than_500_ms_logs_a_warning()
    {
        await using var h = NewHarness();
        using var scope = h.NewScope();

        await scope.ServiceProvider.GetRequiredService<IQueryHandler<SlowQuery, int>>()
            .Handle(new SlowQuery(650), CancellationToken.None);

        var entry = h.Logs.Entries.Single(e => e.Category.Contains("LoggingQueryDecorator"));
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("SlowQuery");
    }

    [Fact]
    public async Task Failing_query_logs_failure_and_rethrows_the_same_type()
    {
        await using var h = NewHarness();
        using var scope = h.NewScope();

        await Should.ThrowAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IQueryHandler<SlowQuery, int>>().Handle(new SlowQuery(0, Throw: true), CancellationToken.None));

        h.Logs.Entries.Single(e => e.Category.Contains("LoggingQueryDecorator")).Message.ShouldContain("failure");
    }

    // registration
    [Fact]
    public async Task AddCqrs_wraps_commands_in_logging_then_transaction_and_queries_in_logging_only()
    {
        await using var h = NewHarness();
        using var scope = h.NewScope();

        var command = scope.ServiceProvider.GetRequiredService<ICommandHandler<SaveTradesCommand, string>>();
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<SlowQuery, int>>();

        command.ShouldBeOfType<LoggingCommandDecorator<SaveTradesCommand, string>>();
        query.ShouldBeOfType<LoggingQueryDecorator<SlowQuery, int>>();

        // the decorators themselves are not registered as handlers
        scope.ServiceProvider.GetServices<ICommandHandler<SaveTradesCommand, string>>().Count().ShouldBe(1);
    }
}
