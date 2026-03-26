using RunOnce.Abstractions;

namespace Sample.Migrations.WorkItems;

[Version("20240103000000")]
[Tags("perf")]
public class AddUserEmailIndexWorkItem : IWorkItem
{
    private readonly IMigrationLogger _logger;

    public AddUserEmailIndexWorkItem(IMigrationLogger logger)
    {
        _logger = logger;
    }

    public Task UpAsync(CancellationToken cancellationToken = default)
    {
        _logger.Log("Creating index on Users.Email...");
        Console.WriteLine("  CREATE UNIQUE INDEX IX_Users_Email ON Users (Email)");
        return Task.CompletedTask;
    }

    public Task DownAsync(CancellationToken cancellationToken = default)
    {
        _logger.Log("Dropping index IX_Users_Email...");
        Console.WriteLine("  DROP INDEX IF EXISTS IX_Users_Email ON Users");
        return Task.CompletedTask;
    }
}
