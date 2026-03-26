using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RunOnce.Abstractions;
using RunOnce.Core.Discovery;
using RunOnce.Core.Execution;
using RunOnce.Persistence.SqlServer;
using Xunit;

namespace RunOnce.Integration.Tests;

/// <summary>
/// Integration tests for SQL Server persistence.
/// Set env var RUNONCE_TEST_SQLSERVER to a valid connection string to run these tests.
/// </summary>
public class SqlServerIntegrationTests
{
    private readonly string? _connectionString = Environment.GetEnvironmentVariable("RUNONCE_TEST_SQLSERVER");

    private SqlServerPersistenceProvider CreateProvider() =>
        new SqlServerPersistenceProvider(_connectionString!);

    private IServiceProvider BuildServices(IPersistenceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(provider);
        services.AddTransient<SimpleWorkItem>();
        services.AddTransient<RollbackWorkItem>();
        return services.BuildServiceProvider();
    }

    [SkippableFact]
    public async Task FullRoundTrip_Up_List_Down()
    {
        Skip.If(_connectionString == null,
            "RUNONCE_TEST_SQLSERVER not set. Skipping SQL Server integration tests.");

        var provider = CreateProvider();
        var services = BuildServices(provider);

        // Cleanup any previous test data
        await provider.EnsureSchemaAsync();
        await provider.RemoveExecutionAsync("INT_SQL_001");
        await provider.RemoveExecutionAsync("INT_SQL_002");

        var descriptors = new[]
        {
            new WorkItemDescriptor { Type = typeof(SimpleWorkItem), Version = "INT_SQL_001" },
            new WorkItemDescriptor { Type = typeof(RollbackWorkItem), Version = "INT_SQL_002" }
        };

        var executor = new RunOnceExecutor(provider, services);

        // Run up
        var upResult = await executor.UpAsync(descriptors);
        upResult.ExecutedVersions.Should().Contain("INT_SQL_001");
        upResult.ExecutedVersions.Should().Contain("INT_SQL_002");
        upResult.SkippedVersions.Should().BeEmpty();

        // Running again should skip all
        var secondResult = await executor.UpAsync(descriptors);
        secondResult.ExecutedVersions.Should().BeEmpty();
        secondResult.SkippedVersions.Should().Contain("INT_SQL_001");
        secondResult.SkippedVersions.Should().Contain("INT_SQL_002");

        // List batches
        var batches = (await provider.GetAllBatchesAsync()).ToList();
        batches.Should().Contain(b => b.BatchId == upResult.BatchId);
        batches.First(b => b.BatchId == upResult.BatchId).ItemCount.Should().Be(2);

        // Roll back batch
        await executor.DownAsync(upResult.BatchId, descriptors);

        // After rollback, items should be executable again
        var thirdResult = await executor.UpAsync(descriptors);
        thirdResult.ExecutedVersions.Should().Contain("INT_SQL_001");
        thirdResult.ExecutedVersions.Should().Contain("INT_SQL_002");

        // Cleanup
        await provider.RemoveExecutionAsync("INT_SQL_001");
        await provider.RemoveExecutionAsync("INT_SQL_002");
    }

    [SkippableFact]
    public async Task Failed_Item_Is_Retryable()
    {
        Skip.If(_connectionString == null,
            "RUNONCE_TEST_SQLSERVER not set. Skipping SQL Server integration tests.");

        var provider = CreateProvider();
        var services = BuildServices(provider);

        await provider.EnsureSchemaAsync();
        await provider.RemoveExecutionAsync("INT_SQL_FAIL");

        var descriptors = new[]
        {
            new WorkItemDescriptor { Type = typeof(FailingWorkItem), Version = "INT_SQL_FAIL" }
        };

        var executor = new RunOnceExecutor(provider, services);

        // First run should fail but record it
        await Assert.ThrowsAsync<WorkItemExecutionException>(() =>
            executor.UpAsync(descriptors));

        // IsExecuted should return false (failure is not considered executed)
        var isExecuted = await provider.IsExecutedAsync("INT_SQL_FAIL");
        isExecuted.Should().BeFalse();

        // Cleanup
        await provider.RemoveExecutionAsync("INT_SQL_FAIL");
    }
}

// ── Integration work items ───────────────────────────────────────────────────

[Version("INT_SQL_001")]
public class SimpleWorkItem : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

[Version("INT_SQL_002")]
public class RollbackWorkItem : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

[Version("INT_SQL_FAIL")]
public class FailingWorkItem : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("Intentional integration test failure.");
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}
