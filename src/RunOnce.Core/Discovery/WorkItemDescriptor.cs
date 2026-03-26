using System;

namespace RunOnce.Core.Discovery;

public class WorkItemDescriptor
{
    public Type Type { get; set; } = null!;
    public string Version { get; set; } = string.Empty;
}
