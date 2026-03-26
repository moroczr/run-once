using System.Collections.Generic;

namespace RunOnce.Core.Execution;

public class UpResult
{
    public string BatchId { get; set; } = string.Empty;
    public IReadOnlyList<string> ExecutedVersions { get; set; } = new List<string>();
    public IReadOnlyList<string> SkippedVersions { get; set; } = new List<string>();
    public IReadOnlyList<string> FailedVersions { get; set; } = new List<string>();
}
