using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RunOnce.Abstractions;
using RunOnce.Core.Discovery;
using RunOnce.Core.Execution;
using Xunit;

namespace RunOnce.Tests;

public class RunOnceExecutorTests
{
    private readonly Mock<IPersistenceProvider> _persistence = new(MockBehavior.Strict);

    private RunOnceExecutor CreateExecutor()
    {
        var services = new ServiceCollection();

        // Register concrete work item types used in tests
        services.AddTransient<AlwaysSucceedsItem>();
        services.AddTransient<AlwaysFailsItem>();

        var provider = services.BuildServiceProvider();
        return new RunOnceExecutor(_persistence.Object, provider);
    }

    [Fact]
    public async Task UpAsync_Skips_Already_Executed_Items()
    {
        _persistence.Setup(p => p.EnsureSchemaAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _persistence.Setup(p => p.IsExecutedAsync("v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var descriptors = new[]
        {
            new WorkItemDescriptor { Type = typeof(AlwaysSucceedsItem), Version = "v1" }
        };

        var executor = CreateExecutor();
        var result = await executor.UpAsync(descriptors);

        result.SkippedVersions.Should().Contain("v1");
        result.ExecutedVersions.Should().BeEmpty();
    }

    [Fact]
    public async Task UpAsync_Executes_Pending_Items_In_Ascending_Order()
    {
        var recordedVersions = new List<string>();

        _persistence.Setup(p => p.EnsureSchemaAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _persistence.Setup(p => p.IsExecutedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _persistence.Setup(p => p.RecordExecutionAsync(It.IsAny<WorkItemRecord>(), It.IsAny<CancellationToken>()))
            .Callback<WorkItemRecord, CancellationToken>((r, _) => recordedVersions.Add(r.Version))
            .Returns(Task.CompletedTask);

        // Intentionally out of order
        var descriptors = new[]
        {
            new WorkItemDescriptor { Type = typeof(AlwaysSucceedsItem), Version = "v3" },
            new WorkItemDescriptor { Type = typeof(AlwaysSucceedsItem), Version = "v1" },
            new WorkItemDescriptor { Type = typeof(AlwaysSucceedsItem), Version = "v2" },
        };

        var executor = CreateExecutor();
        await executor.UpAsync(descriptors);

        recordedVersions.Should().Equal("v1", "v2", "v3");
    }

    [Fact]
    public async Task UpAsync_Throws_WorkItemExecutionException_On_Failure_By_Default()
    {
        _persistence.Setup(p => p.EnsureSchemaAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _persistence.Setup(p => p.IsExecutedAsync("v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _persistence.Setup(p => p.RecordExecutionAsync(It.IsAny<WorkItemRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var descriptors = new[]
        {
            new WorkItemDescriptor { Type = typeof(AlwaysFailsItem), Version = "v1" }
        };

        var executor = CreateExecutor();
        var act = async () => await executor.UpAsync(descriptors);

        await act.Should().ThrowAsync<WorkItemExecutionException>()
            .WithMessage("*v1*");
    }

    [Fact]
    public async Task UpAsync_ContinueOnFailure_Records_Failure_And_Continues()
    {
        _persistence.Setup(p => p.EnsureSchemaAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _persistence.Setup(p => p.IsExecutedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _persistence.Setup(p => p.RecordExecutionAsync(It.IsAny<WorkItemRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var descriptors = new[]
        {
            new WorkItemDescriptor { Type = typeof(AlwaysFailsItem), Version = "v1" },
            new WorkItemDescriptor { Type = typeof(AlwaysSucceedsItem), Version = "v2" }
        };

        var executor = CreateExecutor();
        var result = await executor.UpAsync(descriptors, new UpOptions { ContinueOnFailure = true });

        result.FailedVersions.Should().Contain("v1");
        result.ExecutedVersions.Should().Contain("v2");

        // Verify failure record written with Success=false
        _persistence.Verify(p => p.RecordExecutionAsync(
            It.Is<WorkItemRecord>(r => r.Version == "v1" && !r.Success),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify success record written for v2
        _persistence.Verify(p => p.RecordExecutionAsync(
            It.Is<WorkItemRecord>(r => r.Version == "v2" && r.Success),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpAsync_Returns_BatchId()
    {
        _persistence.Setup(p => p.EnsureSchemaAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _persistence.Setup(p => p.IsExecutedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _persistence.Setup(p => p.RecordExecutionAsync(It.IsAny<WorkItemRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var descriptors = new[]
        {
            new WorkItemDescriptor { Type = typeof(AlwaysSucceedsItem), Version = "v1" }
        };

        var executor = CreateExecutor();
        var result = await executor.UpAsync(descriptors);

        result.BatchId.Should().NotBeNullOrEmpty();
        result.BatchId.Length.Should().Be(26);
    }
}

// ── Helper work items ────────────────────────────────────────────────────────

public class AlwaysSucceedsItem : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class AlwaysFailsItem : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("Intentional failure.");
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

