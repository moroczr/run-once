using System;

namespace RunOnce.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class VersionAttribute : Attribute
{
    public string Version { get; }
    public VersionAttribute(string version) => Version = version;
}
