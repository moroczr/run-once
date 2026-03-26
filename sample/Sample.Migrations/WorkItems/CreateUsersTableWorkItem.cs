using RunOnce.Abstractions;

namespace Sample.Migrations.WorkItems;

[Version("20240101000000")]
public class CreateUsersTableWorkItem : IWorkItem
{
    private readonly IMigrationLogger _logger;

    public CreateUsersTableWorkItem(IMigrationLogger logger)
    {
        _logger = logger;
    }

    public Task UpAsync(CancellationToken cancellationToken = default)
    {
        _logger.Log("Creating Users table...");
        Console.WriteLine("  CREATE TABLE Users (Id INT PRIMARY KEY IDENTITY, Email NVARCHAR(256) NOT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE())");
        return Task.CompletedTask;
    }

    public Task DownAsync(CancellationToken cancellationToken = default)
    {
        _logger.Log("Dropping Users table...");
        Console.WriteLine("  DROP TABLE IF EXISTS Users");
        return Task.CompletedTask;
    }
}
