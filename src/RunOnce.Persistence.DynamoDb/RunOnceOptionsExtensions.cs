using RunOnce.Abstractions;

namespace RunOnce.Persistence.DynamoDb;

public static class RunOnceOptionsExtensions
{
    public static RunOnceOptions UseDynamoDb(this RunOnceOptions options, string regionOrEndpoint)
    {
        return options.UseProvider(new DynamoDbPersistenceProvider(regionOrEndpoint));
    }
}
