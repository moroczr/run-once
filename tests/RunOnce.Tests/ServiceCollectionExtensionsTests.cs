using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RunOnce.Abstractions;
using RunOnce.Core.DependencyInjection;
using Xunit;

namespace RunOnce.Tests;

public class ServiceCollectionExtensionsTests
{
    private static Mock<IPersistenceProvider> StubProvider()
    {
        var mock = new Mock<IPersistenceProvider>();
        mock.Setup(p => p.EnsureSchemaAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    [Fact]
    public void AddRunOnce_Registers_IPersistenceProvider()
    {
        var providerMock = StubProvider();
        var services = new ServiceCollection();

        services.AddRunOnce(o => o.UseProvider(providerMock.Object));

        var sp = services.BuildServiceProvider();
        var resolved = sp.GetService<IPersistenceProvider>();
        resolved.Should().BeSameAs(providerMock.Object);
    }

    [Fact]
    public void AddRunOnce_Registers_Concrete_Provider_Type()
    {
        var providerMock = StubProvider();
        var services = new ServiceCollection();

        services.AddRunOnce(o => o.UseProvider(providerMock.Object));

        var sp = services.BuildServiceProvider();
        var resolved = sp.GetService(providerMock.Object.GetType());
        resolved.Should().BeSameAs(providerMock.Object);
    }

    [Fact]
    public void AddRunOnce_IPersistenceProvider_And_Concrete_Type_Are_Same_Instance()
    {
        var providerMock = StubProvider();
        var services = new ServiceCollection();

        services.AddRunOnce(o => o.UseProvider(providerMock.Object));

        var sp = services.BuildServiceProvider();
        var viaInterface = sp.GetRequiredService<IPersistenceProvider>();
        var viaConcrete = sp.GetRequiredService(providerMock.Object.GetType());

        viaInterface.Should().BeSameAs(viaConcrete);
    }

    [Fact]
    public void AddRunOnce_Without_Provider_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddRunOnce(o => { });

        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*persistence provider*");
    }

    [Fact]
    public void AddRunOnce_With_Assembly_Registers_WorkItems_In_DI()
    {
        var providerMock = StubProvider();
        var services = new ServiceCollection();

        services.AddRunOnce(o => o
            .UseProvider(providerMock.Object)
            .UseAssembly(typeof(DiTestWorkItem).Assembly));

        var sp = services.BuildServiceProvider();

        // Work item must be resolvable both as IWorkItem and as its concrete type
        sp.GetService<DiTestWorkItem>().Should().NotBeNull();
        sp.GetService<IWorkItem>().Should().NotBeNull();
    }

    [Fact]
    public void AddRunOnce_WorkItem_With_Injected_Dependency_Resolves_Correctly()
    {
        var providerMock = StubProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IFakeService, FakeService>();

        services.AddRunOnce(o => o
            .UseProvider(providerMock.Object)
            .UseAssembly(typeof(DiTestWorkItemWithDep).Assembly));

        var sp = services.BuildServiceProvider();

        var item = sp.GetService<DiTestWorkItemWithDep>();
        item.Should().NotBeNull();
        item!.Service.Should().BeOfType<FakeService>();
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

public interface IFakeService { }
public class FakeService : IFakeService { }

[Version("DI_TEST_001")]
public class DiTestWorkItem : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

[Version("DI_TEST_002")]
public class DiTestWorkItemWithDep : IWorkItem
{
    public IFakeService Service { get; }
    public DiTestWorkItemWithDep(IFakeService service) => Service = service;
    public Task UpAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}
