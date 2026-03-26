using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RunOnce.Abstractions;
using RunOnce.Core.Discovery;

namespace RunOnce.Core.Execution;

public class RunOnceExecutor
{
    private readonly IPersistenceProvider _persistence;
    private readonly IServiceProvider _services;
    private readonly ILogger<RunOnceExecutor> _logger;

    public RunOnceExecutor(
        IPersistenceProvider persistence,
        IServiceProvider services,
        ILogger<RunOnceExecutor>? logger = null)
    {
        _persistence = persistence;
        _services = services;
        _logger = logger ?? NullLogger<RunOnceExecutor>.Instance;
    }

    public async Task<UpResult> UpAsync(
        IEnumerable<WorkItemDescriptor> descriptors,
        UpOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new UpOptions();

        await _persistence.EnsureSchemaAsync(ct);

        var batchId = GenerateBatchId();
        var sorted = descriptors.OrderBy(d => d.Version, StringComparer.Ordinal).ToList();

        var executed = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var descriptor in sorted)
        {
            ct.ThrowIfCancellationRequested();

            if (await _persistence.IsExecutedAsync(descriptor.Version, ct))
            {
                _logger.LogDebug("Skipping version '{Version}' (already executed).", descriptor.Version);
                skipped.Add(descriptor.Version);
                continue;
            }

            _logger.LogInformation("Executing work item '{Version}' ({Type}).",
                descriptor.Version, descriptor.Type.FullName);

            IWorkItem instance;
            try
            {
                instance = (IWorkItem)ActivatorUtilities.CreateInstance(_services, descriptor.Type);
            }
            catch (Exception ex)
            {
                var resolveEx = new InvalidOperationException(
                    $"Failed to resolve work item '{descriptor.Version}' ({descriptor.Type.FullName}).", ex);

                await RecordFailureAsync(descriptor, batchId, resolveEx, ct);
                failed.Add(descriptor.Version);

                if (!options.ContinueOnFailure)
                    throw new WorkItemExecutionException(descriptor.Version, resolveEx);

                _logger.LogError(resolveEx, "Work item '{Version}' failed to resolve.", descriptor.Version);
                continue;
            }

            try
            {
                await instance.UpAsync(ct);
            }
            catch (Exception ex)
            {
                await RecordFailureAsync(descriptor, batchId, ex, ct);
                failed.Add(descriptor.Version);

                if (!options.ContinueOnFailure)
                    throw new WorkItemExecutionException(descriptor.Version, ex);

                _logger.LogError(ex, "Work item '{Version}' failed.", descriptor.Version);
                continue;
            }

            await _persistence.RecordExecutionAsync(new WorkItemRecord
            {
                Version = descriptor.Version,
                BatchId = batchId,
                ExecutedAt = DateTimeOffset.UtcNow,
                AssemblyQualifiedName = descriptor.Type.AssemblyQualifiedName ?? descriptor.Type.FullName ?? descriptor.Type.Name,
                Success = true
            }, ct);

            _logger.LogInformation("Work item '{Version}' succeeded.", descriptor.Version);
            executed.Add(descriptor.Version);
        }

        return new UpResult
        {
            BatchId = batchId,
            ExecutedVersions = executed.AsReadOnly(),
            SkippedVersions = skipped.AsReadOnly(),
            FailedVersions = failed.AsReadOnly()
        };
    }

    public async Task DownAsync(
        string batchId,
        IEnumerable<WorkItemDescriptor> descriptors,
        CancellationToken ct = default)
    {
        await _persistence.EnsureSchemaAsync(ct);

        var batchRecords = (await _persistence.GetBatchAsync(batchId, ct)).ToList();
        if (!batchRecords.Any())
        {
            _logger.LogWarning("No records found for batch '{BatchId}'.", batchId);
            return;
        }

        var descriptorMap = descriptors.ToDictionary(d => d.Version, StringComparer.Ordinal);

        // Match batch records to descriptors; sort descending by version
        var toRollback = batchRecords
            .Where(r => r.Success && descriptorMap.ContainsKey(r.Version))
            .OrderByDescending(r => r.Version, StringComparer.Ordinal)
            .ToList();

        foreach (var record in toRollback)
        {
            ct.ThrowIfCancellationRequested();

            var descriptor = descriptorMap[record.Version];
            _logger.LogInformation("Rolling back work item '{Version}'.", record.Version);

            var instance = (IWorkItem)ActivatorUtilities.CreateInstance(_services, descriptor.Type);
            await instance.DownAsync(ct);

            await _persistence.RemoveExecutionAsync(record.Version, ct);
            _logger.LogInformation("Work item '{Version}' rolled back successfully.", record.Version);
        }
    }

    private async Task RecordFailureAsync(
        WorkItemDescriptor descriptor,
        string batchId,
        Exception ex,
        CancellationToken ct)
    {
        try
        {
            await _persistence.RecordExecutionAsync(new WorkItemRecord
            {
                Version = descriptor.Version,
                BatchId = batchId,
                ExecutedAt = DateTimeOffset.UtcNow,
                AssemblyQualifiedName = descriptor.Type.AssemblyQualifiedName ?? descriptor.Type.FullName ?? descriptor.Type.Name,
                Success = false,
                ErrorMessage = ex.Message
            }, ct);
        }
        catch (Exception recordEx)
        {
            _logger.LogError(recordEx, "Failed to record failure for work item '{Version}'.", descriptor.Version);
        }
    }

    private static string GenerateBatchId()
    {
        var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var guid = Guid.NewGuid().ToString("N");
        return $"{ts}-{guid}".Substring(0, 26);
    }
}
