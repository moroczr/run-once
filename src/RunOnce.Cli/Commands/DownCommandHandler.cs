using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RunOnce.Cli.Infrastructure;
using RunOnce.Core.Discovery;
using RunOnce.Core.Execution;

namespace RunOnce.Cli.Commands;

public static class DownCommandHandler
{
    public static async Task<int> HandleAsync(
        string? assemblyPath,
        string? directoryPath,
        string connectionString,
        string? providerName,
        string? providerAssemblyPath,
        string batchId,
        CancellationToken ct)
    {
        try
        {
            var provider = PersistenceProviderFactory.Create(providerName, providerAssemblyPath, connectionString);
            var (services, assemblies) = ContainerBuilder.Build(provider, assemblyPath, directoryPath);

            var discoverer = new WorkItemDiscoverer();
            var descriptors = discoverer.Discover(assemblies);

            var executor = services.GetRequiredService<RunOnceExecutor>();
            await executor.DownAsync(batchId, descriptors, ct);

            Console.WriteLine($"Rollback of batch '{batchId}' completed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
