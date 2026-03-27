using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Testcontainers.DynamoDb;
using Xunit;

namespace RunOnce.Integration.Tests;

public class DynamoDbContainerFixture : IAsyncLifetime
{
    private readonly DynamoDbContainer _container = new DynamoDbBuilder().Build();

    public IAmazonDynamoDB Client { get; private set; } = null!;
    public string Endpoint => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Client = new AmazonDynamoDBClient(
            "test", "test",
            new AmazonDynamoDBConfig { ServiceURL = Endpoint });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class DynamoDbCollection : ICollectionFixture<DynamoDbContainerFixture>
{
    public const string Name = "DynamoDB";
}
