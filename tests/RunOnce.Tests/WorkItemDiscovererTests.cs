using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RunOnce.Abstractions;
using RunOnce.Core.Discovery;
using Xunit;

namespace RunOnce.Tests;

// ── Test work items ──────────────────────────────────────────────────────────

[Version("20240115120000")]
public class WorkItemA : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

[Version("20240116120000")]
public class WorkItemB : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

[Version("20240114120000")]
[Tags("infra", "database")]
public class WorkItemC : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

[Version("20240113120000")]
[Tags("seed")]
public class WorkItemSeedOnly : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

// No [Version] — should be skipped
public class WorkItemNoVersion : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

// ── Tests ────────────────────────────────────────────────────────────────────

public class WorkItemDiscovererTests
{
    private readonly WorkItemDiscoverer _discoverer = new();
    private readonly Assembly _testAssembly = typeof(WorkItemDiscovererTests).Assembly;

    [Fact]
    public void Discover_Returns_Sorted_Ascending_By_Version()
    {
        var results = _discoverer.Discover(new[] { _testAssembly });

        var versions = results.Select(d => d.Version).ToList();
        versions.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void Discover_Skips_Types_Without_Version_Attribute()
    {
        var results = _discoverer.Discover(new[] { _testAssembly });

        results.Should().NotContain(d => d.Type == typeof(WorkItemNoVersion));
    }

    [Fact]
    public void Discover_Throws_On_Duplicate_Versions()
    {
        var action = () => _discoverer.Discover(new[] { _testAssembly, _testAssembly });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate version*");
    }

    [Fact]
    public void Discover_Without_Tags_Returns_All_Versioned_Items()
    {
        var results = _discoverer.Discover(new[] { _testAssembly });

        results.Should().Contain(d => d.Type == typeof(WorkItemA));
        results.Should().Contain(d => d.Type == typeof(WorkItemB));
        results.Should().Contain(d => d.Type == typeof(WorkItemC));
        results.Should().Contain(d => d.Type == typeof(WorkItemSeedOnly));
    }

    [Fact]
    public void Discover_With_Tags_Includes_Matching_Tagged_And_Untagged()
    {
        var results = _discoverer.Discover(new[] { _testAssembly }, tags: new[] { "infra" });

        // WorkItemC has [Tags("infra", "database")] — should be included
        results.Should().Contain(d => d.Type == typeof(WorkItemC));

        // WorkItemA and WorkItemB have no [Tags] — always included
        results.Should().Contain(d => d.Type == typeof(WorkItemA));
        results.Should().Contain(d => d.Type == typeof(WorkItemB));

        // WorkItemSeedOnly has [Tags("seed")] — no match
        results.Should().NotContain(d => d.Type == typeof(WorkItemSeedOnly));
    }

    [Fact]
    public void Discover_Tag_Matching_Is_Case_Insensitive()
    {
        var results = _discoverer.Discover(new[] { _testAssembly }, tags: new[] { "INFRA" });

        results.Should().Contain(d => d.Type == typeof(WorkItemC));
    }

    [Fact]
    public void Discover_With_Multiple_Tags_Includes_Any_Matching()
    {
        var results = _discoverer.Discover(new[] { _testAssembly }, tags: new[] { "seed", "database" });

        results.Should().Contain(d => d.Type == typeof(WorkItemC));
        results.Should().Contain(d => d.Type == typeof(WorkItemSeedOnly));
    }

    [Fact]
    public void Discover_Returns_Correct_Version_Values()
    {
        var results = _discoverer.Discover(new[] { _testAssembly });

        results.Should().Contain(d => d.Version == "20240114120000" && d.Type == typeof(WorkItemC));
        results.Should().Contain(d => d.Version == "20240115120000" && d.Type == typeof(WorkItemA));
        results.Should().Contain(d => d.Version == "20240116120000" && d.Type == typeof(WorkItemB));
    }
}
