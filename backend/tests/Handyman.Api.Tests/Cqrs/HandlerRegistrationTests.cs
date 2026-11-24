using System.Reflection;
using Handyman.Api.Cqrs;
using Handyman.Api.Data;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Handyman.Api.Tests.Cqrs;

/// <summary>Test-only query using the read context, so the wiring check below is not vacuous while the API has no queries.</summary>
public sealed record CountTradesQuery : IQuery<int>;

public sealed class CountTradesHandler(ReadDbContext db) : IQueryHandler<CountTradesQuery, int>
{
    public Task<int> Handle(CountTradesQuery query, CancellationToken ct) => db.Trades.CountAsync(ct);
}

public class HandlerRegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Type[] TestHandlers = [typeof(SaveTradesHandler), typeof(SlowQueryHandler), typeof(CountTradesHandler)];

    private static readonly HashSet<Type> DecoratorDefinitions =
    [
        typeof(LoggingCommandDecorator<,>), typeof(TransactionCommandDecorator<,>), typeof(LoggingQueryDecorator<,>),
    ];

    private static IEnumerable<Type> HandlerInterfaces(Type impl) =>
        impl.GetInterfaces().Where(i => i.IsGenericType &&
            (i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) || i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)));

    private (IServiceProvider Services, IServiceCollection Collection) StartHost()
    {
        IServiceCollection? captured = null;
        var host = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            captured = s;
            s.AddCqrs(TestHandlers);
        }));
        return (host.Services, captured!);
    }

    private static List<Type> AllHandlerClasses() =>
        ArchitectureRules.HandlerClasses(typeof(Program).Assembly.GetTypes()).Concat(TestHandlers).ToList();

    // AC3
    [Fact]
    public void Every_handler_resolves_from_a_scope_wrapped_in_the_decorators_in_the_right_order()
    {
        var (services, _) = StartHost();
        var checkedCount = 0;

        using var scope = services.CreateScope();
        foreach (var impl in AllHandlerClasses())
        foreach (var iface in HandlerInterfaces(impl))
        {
            var isCommand = iface.GetGenericTypeDefinition() == typeof(ICommandHandler<,>);
            var resolved = scope.ServiceProvider.GetRequiredService(iface);

            // outermost first
            Type[] expected = isCommand
                ? [typeof(LoggingCommandDecorator<,>), typeof(TransactionCommandDecorator<,>)]
                : [typeof(LoggingQueryDecorator<,>)];

            var current = resolved;
            foreach (var decorator in expected)
            {
                current.GetType().IsGenericType.ShouldBeTrue($"{iface} is not wrapped by {decorator.Name}");
                current.GetType().GetGenericTypeDefinition().ShouldBe(decorator, $"wrong decorator order for {iface}");
                current = InnerOf(current, iface);
            }
            current.GetType().ShouldBe(impl);
            checkedCount++;
        }

        checkedCount.ShouldBeGreaterThanOrEqualTo(TestHandlers.Length);
    }

    // AC3
    [Fact]
    public void Decorators_and_open_generics_are_never_registered_as_handlers_and_each_use_case_is_registered_once()
    {
        var (_, collection) = StartHost();

        foreach (var d in collection)
        {
            foreach (var type in new[] { d.ServiceType, d.ImplementationType }.OfType<Type>())
            {
                var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
                if (type.IsGenericTypeDefinition)
                    definition.ShouldNotBeOneOf(typeof(ICommandHandler<,>), typeof(IQueryHandler<,>));
                DecoratorDefinitions.ShouldNotContain(definition, $"decorator {type} registered as a service");
            }
        }

        var handlerRegistrations = collection
            .Where(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() is var g
                        && (g == typeof(ICommandHandler<,>) || g == typeof(IQueryHandler<,>)))
            .GroupBy(d => d.ServiceType)
            .ToList();

        handlerRegistrations.ShouldNotBeEmpty();
        handlerRegistrations.ShouldAllBe(g => g.Count() == 1);
    }

    private static object InnerOf(object decorator, Type handlerInterface) =>
        decorator.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(f => f.FieldType == handlerInterface)
            .GetValue(decorator)!;
}
