using RunOnce.Abstractions;

namespace Sample.Migrations.WorkItems;

[Version("20240102000000")]
[Tags("seed")]
public class SeedInitialDataWorkItem : IWorkItem
{
    private readonly IMigrationLogger _logger;

    public SeedInitialDataWorkItem(IMigrationLogger logger)
    {
        _logger = logger;
    }

    public Task UpAsync(CancellationToken cancellationToken = default)
    {
        _logger.Log("Seeding initial admin user...");
        Console.WriteLine("  INSERT INTO Users (Email, CreatedAt) VALUES ('admin@example.com', GETUTCDATE())");
        return Task.CompletedTask;
    }

    public Task DownAsync(CancellationToken cancellationToken = default)
    {
        _logger.Log("Removing seeded admin user...");
        Console.WriteLine("  DELETE FROM Users WHERE Email = 'admin@example.com'");
        return Task.CompletedTask;
    }
}
