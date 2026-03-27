using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.DependencyModel.Resolution;

namespace RunOnce.Cli.Infrastructure;

/// <summary>
/// Hooks into AssemblyLoadContext.Default.Resolving to resolve NuGet package
/// dependencies declared in a user assembly's .deps.json file. Using the default
/// context (rather than an isolated one) ensures all RunOnce.* and framework types
/// share the same Type identity on both sides of the boundary.
/// </summary>
internal sealed class DepsJsonAssemblyLoadContext : IDisposable
{
    private readonly string _assemblyDirectory;
    private readonly DependencyContext? _depContext;
    private readonly CompositeCompilationAssemblyResolver _resolver;
    private readonly Func<AssemblyLoadContext, AssemblyName, Assembly?> _handler;

    public DepsJsonAssemblyLoadContext(string assemblyPath)
    {
        _assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;

        var depsFile = Path.ChangeExtension(assemblyPath, ".deps.json");
        if (File.Exists(depsFile))
        {
            using var stream = File.OpenRead(depsFile);
            _depContext = new DependencyContextJsonReader().Read(stream);
        }

        // Include the CLI's own directory so packages that ship with the tool
        // (e.g. RunOnce.Abstractions) are found even when not in the NuGet cache.
        var cliDirectory = Path.GetDirectoryName(typeof(DepsJsonAssemblyLoadContext).Assembly.Location)!;

        _resolver = new CompositeCompilationAssemblyResolver(
        [
            new AppBaseCompilationAssemblyResolver(_assemblyDirectory),
            new AppBaseCompilationAssemblyResolver(cliDirectory),
            new ReferenceAssemblyPathResolver(),
            new PackageCompilationAssemblyResolver()
        ]);

        // Store handler as a field so the same delegate instance is used for
        // both += and -= (required for event unsubscription to work).
        _handler = OnResolving;
        AssemblyLoadContext.Default.Resolving += _handler;
    }

    public Assembly LoadAssembly(string path)
        => AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        // 1. Check next to the user assembly first
        var local = Path.Combine(_assemblyDirectory, assemblyName.Name + ".dll");
        if (File.Exists(local))
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(local);

        // 2. Probe deps.json entries against NuGet cache and local directories
        if (_depContext == null)
            return null;

        foreach (var lib in _depContext.RuntimeLibraries)
        {
            var wrapper = new CompilationLibrary(
                lib.Type, lib.Name, lib.Version, lib.Hash,
                lib.RuntimeAssemblyGroups.SelectMany(g => g.AssetPaths),
                lib.Dependencies, lib.Serviceable);

            var resolved = new List<string>();
            if (!_resolver.TryResolveAssemblyPaths(wrapper, resolved))
                continue;

            foreach (var candidate in resolved)
            {
                if (string.Equals(
                        Path.GetFileNameWithoutExtension(candidate),
                        assemblyName.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
                }
            }
        }

        return null;
    }

    public void Dispose()
        => AssemblyLoadContext.Default.Resolving -= _handler;
}
