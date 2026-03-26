using System.Reflection;

namespace RunOnce.Abstractions;

public class RunOnceOptions
{
    public Assembly? Assembly { get; private set; }
    public IPersistenceProvider? Provider { get; private set; }

    public RunOnceOptions UseAssembly(Assembly assembly)
    {
        Assembly = assembly;
        return this;
    }

    public RunOnceOptions UseProvider(IPersistenceProvider provider)
    {
        Provider = provider;
        return this;
    }
}
