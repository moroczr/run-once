using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using RunOnce.Abstractions;

namespace RunOnce.Core.DependencyInjection;

public class StartupDiscoverer
{
    public void ConfigureServices(IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        var startupType = typeof(IRunOnceStartup);
        var workItemType = typeof(IWorkItem);

        IRunOnceStartup? startup = null;

        foreach (var assembly in assemblies)
        {
            IEnumerable<Type> types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Select(t => t!);
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (startupType.IsAssignableFrom(type))
                {
                    startup = (IRunOnceStartup)Activator.CreateInstance(type)!;
                }

                if (workItemType.IsAssignableFrom(type))
                {
                    services.AddTransient(workItemType, type);
                    services.AddTransient(type);
                }
            }
        }

        startup?.ConfigureServices(services);
    }
}
