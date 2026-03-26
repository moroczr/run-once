using System.CommandLine;

namespace RunOnce.Cli.Commands;

public static class ListCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("list", "Show all executed work items.");

        cmd.AddOption(SharedOptions.ConnectionStringOption);
        cmd.AddOption(SharedOptions.ProviderOption);
        cmd.AddOption(SharedOptions.ProviderAssemblyOption);

        cmd.SetHandler(async (context) =>
        {
            var connectionString = context.ParseResult.GetValueForOption(SharedOptions.ConnectionStringOption)!;
            var provider = context.ParseResult.GetValueForOption(SharedOptions.ProviderOption);
            var providerAssembly = context.ParseResult.GetValueForOption(SharedOptions.ProviderAssemblyOption);

            context.ExitCode = await ListCommandHandler.HandleAsync(
                connectionString, provider, providerAssembly, context.GetCancellationToken());
        });

        return cmd;
    }
}
