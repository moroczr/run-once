using System.CommandLine;

namespace RunOnce.Cli.Commands;

public static class DownCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("down", "Rollback a specific batch.");

        cmd.AddOption(SharedOptions.AssemblyOption);
        cmd.AddOption(SharedOptions.DirectoryOption);
        cmd.AddOption(SharedOptions.ConnectionStringOption);
        cmd.AddOption(SharedOptions.ProviderOption);
        cmd.AddOption(SharedOptions.ProviderAssemblyOption);

        var batchOption = new Option<string>(
            "--batch",
            "The batch ID to rollback.")
        {
            IsRequired = true
        };
        cmd.AddOption(batchOption);

        cmd.SetHandler(async (context) =>
        {
            var assembly = context.ParseResult.GetValueForOption(SharedOptions.AssemblyOption);
            var directory = context.ParseResult.GetValueForOption(SharedOptions.DirectoryOption);
            var connectionString = context.ParseResult.GetValueForOption(SharedOptions.ConnectionStringOption)!;
            var provider = context.ParseResult.GetValueForOption(SharedOptions.ProviderOption);
            var providerAssembly = context.ParseResult.GetValueForOption(SharedOptions.ProviderAssemblyOption);
            var batchId = context.ParseResult.GetValueForOption(batchOption)!;

            context.ExitCode = await DownCommandHandler.HandleAsync(
                assembly, directory, connectionString, provider, providerAssembly,
                batchId, context.GetCancellationToken());
        });

        return cmd;
    }
}
