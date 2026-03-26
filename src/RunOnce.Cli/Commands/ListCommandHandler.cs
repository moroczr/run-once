using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RunOnce.Cli.Infrastructure;

namespace RunOnce.Cli.Commands;

public static class ListCommandHandler
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
                Console.WriteLine("No executed work items found.");
                return 0;
            }

            foreach (var batch in batches)
            {
                var records = (await provider.GetBatchAsync(batch.BatchId, ct)).ToList();
                foreach (var r in records.OrderBy(x => x.Version))
                {
                    var status = r.Success ? "OK" : "FAILED";
                    Console.WriteLine($"[{status}] {r.Version,-30} batch={r.BatchId}  at={r.ExecutedAt:u}");
                    if (!r.Success && r.ErrorMessage != null)
                        Console.WriteLine($"        Error: {r.ErrorMessage}");
                }
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
