using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RunOnce.Cli.Infrastructure;

namespace RunOnce.Cli.Commands;

public static class ListBatchesCommandHandler
{
    public static async Task<int> HandleAsync(
        string connectionString,
        string? providerName,
        string? providerAssemblyPath,
        CancellationToken ct)
    {
        try
        {
            var provider = PersistenceProviderFactory.Create(providerName, providerAssemblyPath, connectionString);
            await provider.EnsureSchemaAsync(ct);

            var batches = (await provider.GetAllBatchesAsync(ct)).ToList();

            if (!batches.Any())
            {
                Console.WriteLine("No batches found.");
                return 0;
            }

            Console.WriteLine($"{"BatchId",-30} {"StartedAt",-25} {"Items",6}");
            Console.WriteLine(new string('-', 65));
            foreach (var b in batches)
            {
                Console.WriteLine($"{b.BatchId,-30} {b.StartedAt:u,-25} {b.ItemCount,6}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
