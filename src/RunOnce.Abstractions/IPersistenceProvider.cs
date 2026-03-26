using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RunOnce.Abstractions;

public interface IPersistenceProvider
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<bool> IsExecutedAsync(string version, CancellationToken ct = default);
    Task RecordExecutionAsync(WorkItemRecord record, CancellationToken ct = default);
    Task RemoveExecutionAsync(string version, CancellationToken ct = default);
    Task<IEnumerable<WorkItemRecord>> GetBatchAsync(string batchId, CancellationToken ct = default);
    Task<IEnumerable<BatchSummary>> GetAllBatchesAsync(CancellationToken ct = default);
}
