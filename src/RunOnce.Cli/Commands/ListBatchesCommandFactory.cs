using System.CommandLine;

namespace RunOnce.Cli.Commands;

public static class ListBatchesCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("list-batches", "Show all batches.");

        cmd.AddOption(SharedOptions.ConnectionStringOption);
        cmd.AddOption(SharedOptions.ProviderOption);
        cmd.AddOption(SharedOptions.ProviderAssemblyOption);

        cmd.SetHandler(async (context) =>
        {
            var connectionString = context.ParseResult.GetValueForOption(SharedOptions.ConnectionStringOption)!;
            var provider = context.ParseResult.GetValueForOption(SharedOptions.ProviderOption);
            var providerAssembly = context.ParseResult.GetValueForOption(SharedOptions.ProviderAssemblyOption);

            context.ExitCode = await ListBatchesCommandHandler.HandleAsync(
                connectionString, provider, providerAssembly, context.GetCancellationToken());
        });

        return cmd;
    }
}
