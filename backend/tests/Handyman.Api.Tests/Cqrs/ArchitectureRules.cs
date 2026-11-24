using System.Reflection;
using System.Text.RegularExpressions;
using Handyman.Api.Cqrs;
using Handyman.Api.Data;

namespace Handyman.Api.Tests.Cqrs;

/// <summary>The CQRS architecture rules as plain reflection over a set of types, so the same rules can run on
/// the real API assembly and on synthetic types that must break them. Each rule returns violation messages.</summary>
public static class ArchitectureRules
{
    private const BindingFlags AllInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static IEnumerable<Type> Concrete(IEnumerable<Type> types) =>
        types.Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

    private static IEnumerable<Type> Implemented(Type type, Type openInterface) =>
        type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);

    public static IReadOnlyList<Type> CommandTypes(IEnumerable<Type> types) =>
        Concrete(types).Where(t => Implemented(t, typeof(ICommand<>)).Any()).ToList();

    public static IReadOnlyList<Type> QueryTypes(IEnumerable<Type> types) =>
        Concrete(types).Where(t => Implemented(t, typeof(IQuery<>)).Any()).ToList();

    /// <summary>Concrete handler classes (internal ones included, decorators and other open generics excluded).</summary>
    public static IReadOnlyList<Type> HandlerClasses(IEnumerable<Type> types) =>
        Concrete(types).Where(t => Implemented(t, typeof(ICommandHandler<,>)).Any()
                                   || Implemented(t, typeof(IQueryHandler<,>)).Any()).ToList();

    /// <summary>Every command and query type has exactly one handler, counted per handler interface
    /// (a class implementing two handler interfaces counts once for each of its commands).</summary>
    public static List<string> HandlerCountViolations(IEnumerable<Type> types)
    {
        var all = types.ToList();
        var handlers = HandlerClasses(all);
        var violations = new List<string>();

        void Check(IEnumerable<Type> requests, Type requestInterface, Type handlerInterface)
        {
            foreach (var request in requests)
            foreach (var result in Implemented(request, requestInterface).Select(i => i.GetGenericArguments()[0]))
            {
                var closed = handlerInterface.MakeGenericType(request, result);
                var found = handlers.Where(h => closed.IsAssignableFrom(h)).ToList();
                if (found.Count == 0)
                    violations.Add($"{request.FullName} has no handler");
                else if (found.Count > 1)
                    violations.Add($"{request.FullName} has {found.Count} handlers: {string.Join(", ", found.Select(f => f.FullName))}");
            }
        }

        Check(CommandTypes(all), typeof(ICommand<>), typeof(ICommandHandler<,>));
        Check(QueryTypes(all), typeof(IQuery<>), typeof(IQueryHandler<,>));
        return violations;
    }

    /// <summary>A command handler must not touch ReadDbContext; a query handler must not touch AppDbContext
    /// (constructor parameters and instance fields, also inside generic arguments such as Func or IDbContextFactory).</summary>
    public static List<string> ContextViolations(IEnumerable<Type> types)
    {
        var violations = new List<string>();
        foreach (var handler in HandlerClasses(types))
        {
            var isCommand = Implemented(handler, typeof(ICommandHandler<,>)).Any();
            var isQuery = Implemented(handler, typeof(IQueryHandler<,>)).Any();

            if (isCommand && Dependencies(handler).Any(d => Mentions(d, typeof(ReadDbContext))))
                violations.Add($"command handler {handler.FullName} depends on ReadDbContext");
            if (isQuery && Dependencies(handler).Any(d => Mentions(d, typeof(AppDbContext))))
                violations.Add($"query handler {handler.FullName} depends on AppDbContext");
        }
        return violations;
    }

    private static IEnumerable<Type> Dependencies(Type handler)
    {
        for (var t = handler; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var ctor in t.GetConstructors(AllInstance))
            foreach (var p in ctor.GetParameters())
                yield return p.ParameterType;
            foreach (var f in t.GetFields(AllInstance))
                yield return f.FieldType;
        }
    }

    private static bool Mentions(Type type, Type target)
    {
        if (type == target) return true;
        if (type.HasElementType && Mentions(type.GetElementType()!, target)) return true;
        return type.IsGenericType && type.GetGenericArguments().Any(a => Mentions(a, target));
    }

    // ---- no hand-made contexts (US-0015 limit 1) ----

    private static readonly Regex HandMade = new(@"\bnew\s+(AppDbContext|ReadDbContext)\s*\(", RegexOptions.Compiled);

    /// <summary>Source files (path -> text) that construct a database context themselves instead of getting it from DI.</summary>
    public static List<string> HandMadeContextViolations(IEnumerable<KeyValuePair<string, string>> sources)
    {
        var violations = new List<string>();
        foreach (var (path, text) in sources)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//")) continue;
                if (HandMade.IsMatch(lines[i])) violations.Add($"{path}:{i + 1} constructs a database context directly");
            }
        }
        return violations;
    }
}
