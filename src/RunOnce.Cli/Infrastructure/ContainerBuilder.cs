using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using RunOnce.Abstractions;
using RunOnce.Core.DependencyInjection;
using RunOnce.Core.Execution;

namespace RunOnce.Cli.Infrastructure;

public static class ContainerBuilder
{
    public static (IServiceProvider Services, IReadOnlyList<Assembly> Assemblies) Build(
        IPersistenceProvider provider,
        string? assemblyPath,
        string? directoryPath)
    {
        var services = new ServiceCollection();

        services.AddSingleton(provider);

        var loader = new RunOnce.Core.Discovery.AssemblyLoader();
        var assemblies = new List<Assembly>();

        if (assemblyPath != null)
            assemblies.Add(loader.LoadFrom(assemblyPath));

        if (directoryPath != null)
            assemblies.AddRange(loader.LoadFromDirectory(directoryPath));

        if (assemblies.Count > 0)
        {
            var discoverer = new StartupDiscoverer();
            discoverer.ConfigureServices(services, assemblies);
        }

        services.AddTransient<RunOnceExecutor>();

        return (services.BuildServiceProvider(), assemblies.AsReadOnly());
    }
}
