using System;

namespace RunOnce.Abstractions;

public class WorkItemRecord
{
    public string Version { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public DateTimeOffset ExecutedAt { get; set; }
    public string AssemblyQualifiedName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
