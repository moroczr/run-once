using RunOnce.Abstractions;

namespace RunOnce.Persistence.SqlServer;

public static class RunOnceOptionsExtensions
{
    public static RunOnceOptions UseSqlServer(this RunOnceOptions options, string connectionString)
    {
        return options.UseProvider(new SqlServerPersistenceProvider(connectionString));
    }
}
