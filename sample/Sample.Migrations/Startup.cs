using Microsoft.Extensions.DependencyInjection;
using RunOnce.Abstractions;

namespace Sample.Migrations;

public interface IMigrationLogger
{
    void Log(string message);
}

public class ConsoleMigrationLogger : IMigrationLogger
{
    public void Log(string message) => Console.WriteLine($"[MigrationLogger] {message}");
}

public class Startup : IRunOnceStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMigrationLogger, ConsoleMigrationLogger>();
    }
}
