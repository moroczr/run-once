using System.CommandLine;

namespace RunOnce.Cli.Commands;

public static class SharedOptions
{
    public static readonly Option<string?> AssemblyOption = new(
        "--assembly",
        "Path to the assembly containing work items.");

    public static readonly Option<string?> DirectoryOption = new(
        "--directory",
        "Path to directory containing assemblies with work items.");

    public static readonly Option<string> ConnectionStringOption = new(
        "--connection-string",
        "Connection string (or region for DynamoDB).")
    {
        IsRequired = true
    };

    public static readonly Option<string?> ProviderOption = new(
        "--provider",
        "Persistence provider: 'sql' or 'dynamo'.");

    public static readonly Option<string?> ProviderAssemblyOption = new(
        "--provider-assembly",
        "Path to assembly containing a custom IPersistenceProvider implementation.");
}
