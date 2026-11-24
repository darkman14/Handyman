using Handyman.Api.Cqrs;
using Handyman.Api.Data;
using Shouldly;

namespace Handyman.Api.Tests.Cqrs;

// ---- synthetic use cases: only ever put in explicit type lists, never registered as real handlers ----
internal static class Fakes
{
    public sealed record OrphanCommand : ICommand<int>;
    public sealed record OrphanQuery : IQuery<int>;

    public sealed record TwinCommand : ICommand<Unit>;
    public sealed class TwinHandlerA : ICommandHandler<TwinCommand, Unit>
    {
        public Task<Unit> Handle(TwinCommand command, CancellationToken ct) => Task.FromResult(Unit.Value);
    }
    public sealed class TwinHandlerB : ICommandHandler<TwinCommand, Unit>
    {
        public Task<Unit> Handle(TwinCommand command, CancellationToken ct) => Task.FromResult(Unit.Value);
    }

    public sealed record CommandOne : ICommand<Unit>;
    public sealed record CommandTwo : ICommand<Unit>;
    /// <summary>One internal class handling two commands: fine, each command still has exactly one handler.</summary>
    internal sealed class DualHandler : ICommandHandler<CommandOne, Unit>, ICommandHandler<CommandTwo, Unit>
    {
        public Task<Unit> Handle(CommandOne command, CancellationToken ct) => Task.FromResult(Unit.Value);
        public Task<Unit> Handle(CommandTwo command, CancellationToken ct) => Task.FromResult(Unit.Value);
    }

    public sealed record QueryOne : IQuery<int>;
    internal sealed class GoodQueryHandler(ReadDbContext db) : IQueryHandler<QueryOne, int>
    {
        public Task<int> Handle(QueryOne query, CancellationToken ct) => Task.FromResult(db.GetHashCode());
    }
    internal sealed class GoodCommandHandler(AppDbContext db) : ICommandHandler<TwinCommand, Unit>
    {
        public AppDbContext Db { get; } = db;
        public Task<Unit> Handle(TwinCommand command, CancellationToken ct) => Task.FromResult(Unit.Value);
    }

    public sealed record BadQuery : IQuery<int>;
    internal sealed class QueryUsingAppDb(AppDbContext db) : IQueryHandler<BadQuery, int>
    {
        public Task<int> Handle(BadQuery query, CancellationToken ct) => Task.FromResult(db.GetHashCode());
    }

    public sealed record BadQueryFactory : IQuery<int>;
    internal sealed class QueryUsingAppDbFactory(Func<AppDbContext> make) : IQueryHandler<BadQueryFactory, int>
    {
        public Task<int> Handle(BadQueryFactory query, CancellationToken ct) => Task.FromResult(make().GetHashCode());
    }

    public sealed record BadQueryField : IQuery<int>;
    internal sealed class QueryWithAppDbField : IQueryHandler<BadQueryField, int>
    {
        private readonly AppDbContext? _db = null;
        public Task<int> Handle(BadQueryField query, CancellationToken ct) => Task.FromResult(_db?.GetHashCode() ?? 0);
    }

    public sealed record BadCommand : ICommand<Unit>;
    internal sealed class CommandUsingReadDb(ReadDbContext db) : ICommandHandler<BadCommand, Unit>
    {
        public ReadDbContext Db { get; } = db;
        public Task<Unit> Handle(BadCommand command, CancellationToken ct) => Task.FromResult(Unit.Value);
    }
}

public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Api = typeof(Program).Assembly;

    // ---- AC1: exactly one handler per command and query ----

    [Fact]
    public void Every_command_and_query_in_the_api_has_exactly_one_handler()
    {
        ArchitectureRules.HandlerCountViolations(Api.GetTypes()).ShouldBeEmpty();
    }

    [Fact]
    public void A_command_without_a_handler_is_reported()
    {
        var violations = ArchitectureRules.HandlerCountViolations([typeof(Fakes.OrphanCommand)]);

        violations.Count.ShouldBe(1);
        violations[0].ShouldContain("OrphanCommand has no handler");
    }

    [Fact]
    public void A_query_without_a_handler_is_reported()
    {
        var violations = ArchitectureRules.HandlerCountViolations([typeof(Fakes.OrphanQuery)]);

        violations.Count.ShouldBe(1);
        violations[0].ShouldContain("OrphanQuery has no handler");
    }

    [Fact]
    public void Two_handlers_for_one_command_are_reported()
    {
        var violations = ArchitectureRules.HandlerCountViolations(
            [typeof(Fakes.TwinCommand), typeof(Fakes.TwinHandlerA), typeof(Fakes.TwinHandlerB)]);

        violations.Count.ShouldBe(1);
        violations[0].ShouldContain("TwinCommand has 2 handlers");
    }

    [Fact]
    public void An_internal_class_handling_two_commands_counts_as_one_handler_for_each()
    {
        ArchitectureRules.HandlerCountViolations(
            [typeof(Fakes.CommandOne), typeof(Fakes.CommandTwo), typeof(Fakes.DualHandler)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_command_whose_handler_is_not_in_the_scanned_set_is_reported_as_missing()
    {
        var violations = ArchitectureRules.HandlerCountViolations([typeof(Fakes.CommandOne)]);

        violations.Count.ShouldBe(1);
        violations[0].ShouldContain("CommandOne has no handler");
    }

    [Fact]
    public void The_decorators_are_not_mistaken_for_handlers()
    {
        ArchitectureRules.HandlerClasses(Api.GetTypes()).ShouldNotContain(t =>
            t.IsGenericType || t.Name.Contains("Decorator"));
    }

    // ---- AC2: right database context per side ----

    [Fact]
    public void No_query_handler_depends_on_AppDbContext_and_no_command_handler_on_ReadDbContext()
    {
        ArchitectureRules.ContextViolations(Api.GetTypes()).ShouldBeEmpty();
    }

    [Fact]
    public void Correctly_wired_handlers_pass()
    {
        ArchitectureRules.ContextViolations(
            [typeof(Fakes.GoodQueryHandler), typeof(Fakes.GoodCommandHandler), typeof(Fakes.DualHandler)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_query_handler_taking_AppDbContext_is_reported()
    {
        var violations = ArchitectureRules.ContextViolations([typeof(Fakes.QueryUsingAppDb)]);

        violations.Count.ShouldBe(1);
        violations[0].ShouldContain("query handler");
        violations[0].ShouldContain("AppDbContext");
    }

    [Fact]
    public void A_query_handler_reaching_AppDbContext_through_a_factory_or_a_field_is_reported()
    {
        ArchitectureRules.ContextViolations([typeof(Fakes.QueryUsingAppDbFactory)]).Count.ShouldBe(1);
        ArchitectureRules.ContextViolations([typeof(Fakes.QueryWithAppDbField)]).Count.ShouldBe(1);
    }

    [Fact]
    public void A_command_handler_taking_ReadDbContext_is_reported()
    {
        var violations = ArchitectureRules.ContextViolations([typeof(Fakes.CommandUsingReadDb)]);

        violations.Count.ShouldBe(1);
        violations[0].ShouldContain("command handler");
        violations[0].ShouldContain("ReadDbContext");
    }

    // ---- US-0015 limit 1: contexts come from DI, never from `new` ----

    [Fact]
    public void Nothing_in_the_api_source_constructs_a_database_context_itself()
    {
        var root = FindApiSourceRoot();
        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => KeyValuePair.Create(f, File.ReadAllText(f)))
            .ToList();

        sources.ShouldNotBeEmpty(); // guards against scanning the wrong folder
        ArchitectureRules.HandMadeContextViolations(sources).ShouldBeEmpty();
    }

    [Fact]
    public void A_hand_made_context_in_source_text_is_reported_but_comments_and_other_types_are_not()
    {
        var violations = ArchitectureRules.HandMadeContextViolations(
        [
            KeyValuePair.Create("A.cs", "var a = new AppDbContext(options);\n"),
            KeyValuePair.Create("B.cs", "x();\nvar b = new  ReadDbContext (options);\n"),
            KeyValuePair.Create("C.cs", "// never write new AppDbContext(options)\nvar c = new SomeOtherContext(o);\n"),
        ]);

        violations.Count.ShouldBe(2);
        violations[0].ShouldStartWith("A.cs:1");
        violations[1].ShouldStartWith("B.cs:2");
    }

    private static string FindApiSourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Handyman.Api");
            if (File.Exists(Path.Combine(candidate, "Handyman.Api.csproj"))) return candidate;
        }
        throw new DirectoryNotFoundException("backend/src/Handyman.Api not found above the test output folder");
    }
}
