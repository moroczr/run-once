using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RunOnce.Core.Discovery;

public class AssemblyLoader
{
    private readonly ILogger<AssemblyLoader> _logger;

    public AssemblyLoader(ILogger<AssemblyLoader>? logger = null)
    {
        _logger = logger ?? NullLogger<AssemblyLoader>.Instance;
    }

    public Assembly LoadFrom(string path)
    {
        return Assembly.LoadFrom(path);
    }

    public IReadOnlyList<Assembly> LoadFromDirectory(string dir)
    {
        var loaded = new List<Assembly>();
        foreach (var dll in Directory.GetFiles(dir, "*.dll"))
        {
            try
            {
                loaded.Add(Assembly.LoadFrom(dll));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load assembly from '{Path}'. Skipping.", dll);
            }
        }
        return loaded.AsReadOnly();
    }
}
