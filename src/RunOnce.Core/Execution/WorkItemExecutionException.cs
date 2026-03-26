using System;

namespace RunOnce.Core.Execution;

public class WorkItemExecutionException : Exception
{
    public string Version { get; }

    public WorkItemExecutionException(string version, Exception inner)
        : base($"Work item '{version}' failed: {inner.Message}", inner)
    {
        Version = version;
    }
}
