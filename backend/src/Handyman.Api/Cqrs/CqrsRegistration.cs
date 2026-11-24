using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Handyman.Api.Cqrs;

public static class CqrsRegistration
{
    /// <summary>Registers every command/query handler in the assembly, wrapped in its decorators:
    /// commands logging -> transaction -> handler, queries logging -> handler.</summary>
    public static IServiceCollection AddCqrs(this IServiceCollection services, Assembly assembly)
    {
        services.TryAddScoped<PendingPushes>();
        services.TryAddSingleton<IPushQueue, PushQueue>();

        // skip open generics, otherwise the decorators would register themselves as handlers
        var types = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var impl in types)
        foreach (var iface in impl.GetInterfaces().Where(i => i.IsGenericType))
        {
            var def = iface.GetGenericTypeDefinition();
            Type[] decorators =
                def == typeof(ICommandHandler<,>) ? [typeof(TransactionCommandDecorator<,>), typeof(LoggingCommandDecorator<,>)] :
                def == typeof(IQueryHandler<,>) ? [typeof(LoggingQueryDecorator<,>)] :
                [];
            if (decorators.Length == 0) continue;

            var args = iface.GetGenericArguments();
            services.AddScoped(impl);
            services.AddScoped(iface, sp =>
            {
                var inner = sp.GetRequiredService(impl);
                foreach (var d in decorators) // innermost first
                    inner = ActivatorUtilities.CreateInstance(sp, d.MakeGenericType(args), inner);
                return inner;
            });
        }
        return services;
    }
}
