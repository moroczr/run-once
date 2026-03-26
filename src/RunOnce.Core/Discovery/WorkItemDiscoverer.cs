using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunOnce.Abstractions;

namespace RunOnce.Core.Discovery;

public class WorkItemDiscoverer
{
    public IReadOnlyList<WorkItemDescriptor> Discover(
        IEnumerable<Assembly> assemblies,
        string[]? tags = null)
    {
        var workItemType = typeof(IWorkItem);
        var versionAttributeType = typeof(VersionAttribute);
        var tagsAttributeType = typeof(TagsAttribute);

        var all = new List<WorkItemDescriptor>();

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

                if (!workItemType.IsAssignableFrom(type))
                    continue;

                var versionAttr = type.GetCustomAttribute<VersionAttribute>(inherit: false);
                if (versionAttr == null)
                    continue;

                all.Add(new WorkItemDescriptor
                {
                    Type = type,
                    Version = versionAttr.Version
                });
            }
        }

        // Check for duplicate versions
        var duplicates = all
            .GroupBy(d => d.Version)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Any())
        {
            var messages = duplicates.Select(g =>
                $"Version '{g.Key}' is defined by: {string.Join(", ", g.Select(d => d.Type.FullName))}");
            throw new InvalidOperationException(
                $"Duplicate version(s) detected:\n{string.Join("\n", messages)}");
        }

        // Apply tag filtering if tags specified
        IEnumerable<WorkItemDescriptor> filtered = all;
        if (tags != null && tags.Length > 0)
        {
            filtered = all.Where(d =>
            {
                var tagsAttr = d.Type.GetCustomAttribute<TagsAttribute>(inherit: false);
                if (tagsAttr == null)
                    return true; // untagged items always run

                return tagsAttr.Tags.Any(t =>
                    tags.Any(f => string.Equals(f, t, StringComparison.OrdinalIgnoreCase)));
            });
        }

        return filtered
            .OrderBy(d => d.Version, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }
}
