using System.CommandLine;
using RunOnce.Cli.Commands;

var rootCommand = new RootCommand("run-once — execute versioned work items once.");

rootCommand.AddCommand(UpCommandFactory.Create());
rootCommand.AddCommand(DownCommandFactory.Create());
rootCommand.AddCommand(ListCommandFactory.Create());
rootCommand.AddCommand(ListBatchesCommandFactory.Create());

return await rootCommand.InvokeAsync(args);
