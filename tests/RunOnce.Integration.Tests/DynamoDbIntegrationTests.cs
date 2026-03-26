using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RunOnce.Abstractions;
using RunOnce.Core.Discovery;
using RunOnce.Core.Execution;
using RunOnce.Persistence.DynamoDb;
using Xunit;

namespace RunOnce.Integration.Tests;

/// <summary>
/// Integration tests for DynamoDB persistence.
/// Set env var RUNONCE_TEST_DYNAMODB to a region or LocalStack endpoint (e.g. http://localhost:4566).
/// </summary>
public class DynamoDbIntegrationTests
{
    private readonly string? _endpoint = Environment.GetEnvironmentVariable("RUNONCE_TEST_DYNAMODB");

    private DynamoDbPersistenceProvider CreateProvider() =>
        new DynamoDbPersistenceProvider(_endpoint!);

    private IServiceProvider BuildServices(IPersistenceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(provider);
        services.AddTransient<DynSimpleWorkItem>();
        services.AddTransient<DynRollbackWorkItem>();
        return services.BuildServiceProvider();
    }

    [SkippableFact]
    public async Task FullRoundTrip_Up_List_Down()
    {
        Skip.If(_endpoint == null,
            "RUNONCE_TEST_DYNAMODB not set. Skipping DynamoDB integration tests.");

        var provider = CreateProvider();
        var services = BuildServices(provider);

        await provider.EnsureSchemaAsync();

        // Cleanup any previous test data
        await provider.RemoveExecutionAsync("INT_DYN_001");
        await provider.RemoveExecutionAsync("INT_DYN_002");

        var descriptors = new[]
        {
            new WorkItemDescriptor { Type = typeof(DynSimpleWorkItem), Version = "INT_DYN_001" },
            new WorkItemDescriptor { Type = typeof(DynRollbackWorkItem), Version = "INT_DYN_002" }
        };

        var executor = new RunOnceExecutor(provider, services);

        // Run up
        var upResult = await executor.UpAsync(descriptors);
        upResult.ExecutedVersions.Should().Contain("INT_DYN_001");
        upResult.ExecutedVersions.Should().Contain("INT_DYN_002");
        upResult.SkippedVersions.Should().BeEmpty();

        // Running again should skip all
        var secondResult = await executor.UpAsync(descriptors);
        secondResult.ExecutedVersions.Should().BeEmpty();
        secondResult.SkippedVersions.Should().Contain("INT_DYN_001");
        secondResult.SkippedVersions.Should().Contain("INT_DYN_002");

        // List batches
        var batches = (await provider.GetAllBatchesAsync()).ToList();
        batches.Should().Contain(b => b.BatchId == upResult.BatchId);

        // Roll back batch
        await executor.DownAsync(upResult.BatchId, descriptors);

        // After rollback, items should be executable again
        var thirdResult = await executor.UpAsync(descriptors);
        thirdResult.ExecutedVersions.Should().Contain("INT_DYN_001");
        thirdResult.ExecutedVersions.Should().Contain("INT_DYN_002");

        // Cleanup
        await provider.RemoveExecutionAsync("INT_DYN_001");
        await provider.RemoveExecutionAsync("INT_DYN_002");
    }
}

// ── DynamoDB integration work items ─────────────────────────────────────────

[Version("INT_DYN_001")]
public class DynSimpleWorkItem : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

[Version("INT_DYN_002")]
public class DynRollbackWorkItem : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}
