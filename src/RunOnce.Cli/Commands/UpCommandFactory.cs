using System.CommandLine;

namespace RunOnce.Cli.Commands;

public static class UpCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("up", "Execute pending work items.");

        cmd.AddOption(SharedOptions.AssemblyOption);
        cmd.AddOption(SharedOptions.DirectoryOption);
        cmd.AddOption(SharedOptions.ConnectionStringOption);
        cmd.AddOption(SharedOptions.ProviderOption);
        cmd.AddOption(SharedOptions.ProviderAssemblyOption);

        var continueOnFailureOption = new Option<bool>(
            "--continue-on-failure",
            "Log failures and continue instead of halting.");
        cmd.AddOption(continueOnFailureOption);

        var tagsOption = new Option<string[]>(
            "--tags",
            "Only execute work items matching at least one of these tags (untagged items always run).")
        {
            AllowMultipleArgumentsPerToken = true
        };
        cmd.AddOption(tagsOption);

        cmd.SetHandler(async (context) =>
        {
            var assembly = context.ParseResult.GetValueForOption(SharedOptions.AssemblyOption);
            var directory = context.ParseResult.GetValueForOption(SharedOptions.DirectoryOption);
            var connectionString = context.ParseResult.GetValueForOption(SharedOptions.ConnectionStringOption)!;
            var provider = context.ParseResult.GetValueForOption(SharedOptions.ProviderOption);
            var providerAssembly = context.ParseResult.GetValueForOption(SharedOptions.ProviderAssemblyOption);
            var continueOnFailure = context.ParseResult.GetValueForOption(continueOnFailureOption);
            var tags = context.ParseResult.GetValueForOption(tagsOption);

            context.ExitCode = await UpCommandHandler.HandleAsync(
                assembly, directory, connectionString, provider, providerAssembly,
                continueOnFailure, tags, context.GetCancellationToken());
        });

        return cmd;
    }
}
