using Amazon.DynamoDBv2;
using RunOnce.Abstractions;

namespace RunOnce.Persistence.DynamoDb;

public static class RunOnceOptionsExtensions
{
    public static RunOnceOptions UseDynamoDb(this RunOnceOptions options, string regionOrEndpoint)
    {
        return options.UseProvider(new DynamoDbPersistenceProvider(regionOrEndpoint));
    }

    public static RunOnceOptions UseDynamoDb(this RunOnceOptions options, IAmazonDynamoDB client)
    {
        return options.UseProvider(new DynamoDbPersistenceProvider(client));
    }
}
