using System;

namespace RunOnce.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TagsAttribute : Attribute
{
    public string[] Tags { get; }
    public TagsAttribute(params string[] tags) => Tags = tags;
}
