using RunOnce.Abstractions;

namespace Sample.Migrations.WorkItems;

[Version("20240104000000")]
[Tags("seed")]
public class UpdateAppConfigWorkItem : IWorkItem
{
    private readonly IMigrationLogger _logger;

    public UpdateAppConfigWorkItem(IMigrationLogger logger)
    {
        _logger = logger;
    }

    public Task UpAsync(CancellationToken cancellationToken = default)
    {
        _logger.Log("Inserting default app config row...");
        Console.WriteLine("  INSERT INTO AppConfig (Key, Value) VALUES ('MaintenanceMode', 'false'), ('MaxUploadSizeMb', '10')");
        return Task.CompletedTask;
    }

    public Task DownAsync(CancellationToken cancellationToken = default)
    {
        _logger.Log("Removing default app config rows...");
        Console.WriteLine("  DELETE FROM AppConfig WHERE Key IN ('MaintenanceMode', 'MaxUploadSizeMb')");
        return Task.CompletedTask;
    }
}
