using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RunOnce.Cli.Infrastructure;
using RunOnce.Core.Discovery;
using RunOnce.Core.Execution;

namespace RunOnce.Cli.Commands;

public static class UpCommandHandler
{
    public static async Task<int> HandleAsync(
        string? assemblyPath,
        string? directoryPath,
        string connectionString,
        string? providerName,
        string? providerAssemblyPath,
        bool continueOnFailure,
        string[]? tags,
        CancellationToken ct)
    {
        try
        {
            var provider = PersistenceProviderFactory.Create(providerName, providerAssemblyPath, connectionString);
            var (services, assemblies) = ContainerBuilder.Build(provider, assemblyPath, directoryPath);

            var discoverer = new WorkItemDiscoverer();
            var descriptors = discoverer.Discover(assemblies, tags?.Length > 0 ? tags : null);

            if (descriptors.Count == 0)
            {
                Console.WriteLine("No work items discovered.");
                return 0;
            }

            var executor = services.GetRequiredService<RunOnceExecutor>();
            var result = await executor.UpAsync(descriptors, new UpOptions
            {
                ContinueOnFailure = continueOnFailure
            }, ct);

            Console.WriteLine($"Batch: {result.BatchId}");
            Console.WriteLine($"Executed : {result.ExecutedVersions.Count}");
            Console.WriteLine($"Skipped  : {result.SkippedVersions.Count}");
            Console.WriteLine($"Failed   : {result.FailedVersions.Count}");

            foreach (var v in result.ExecutedVersions)
                Console.WriteLine($"  [OK]      {v}");
            foreach (var v in result.SkippedVersions)
                Console.WriteLine($"  [SKIP]    {v}");
            foreach (var v in result.FailedVersions)
                Console.WriteLine($"  [FAILED]  {v}");

            return result.FailedVersions.Count > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
