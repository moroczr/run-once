using System;

namespace RunOnce.Abstractions;

public class BatchSummary
{
    public string BatchId { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public int ItemCount { get; set; }
}
