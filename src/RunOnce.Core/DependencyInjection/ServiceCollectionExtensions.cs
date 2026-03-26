using System;
using Microsoft.Extensions.DependencyInjection;
using RunOnce.Abstractions;
using RunOnce.Core.Execution;

namespace RunOnce.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRunOnce(
        this IServiceCollection services,
        Action<RunOnceOptions> configure)
    {
        var options = new RunOnceOptions();
        configure(options);

        if (options.Provider == null)
            throw new InvalidOperationException(
                "A persistence provider must be configured. Call options.UseProvider(...) or a provider-specific extension method.");

        services.AddSingleton(options.Provider);
        services.AddSingleton<IPersistenceProvider>(sp => sp.GetRequiredService<IPersistenceProvider>());

        if (options.Assembly != null)
        {
            var discoverer = new StartupDiscoverer();
            discoverer.ConfigureServices(services, new[] { options.Assembly });
        }

        services.AddTransient<RunOnceExecutor>();

        return services;
    }
}
