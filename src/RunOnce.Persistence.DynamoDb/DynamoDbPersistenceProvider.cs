using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using RunOnce.Abstractions;

namespace RunOnce.Persistence.DynamoDb;

public class DynamoDbPersistenceProvider : IPersistenceProvider
{
    private const string TableName = "RunOnceHistory";
    private const string GsiName = "Version-index";

    private readonly IAmazonDynamoDB _client;

    public DynamoDbPersistenceProvider(string regionOrEndpoint)
    {
        if (regionOrEndpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            // LocalStack or custom endpoint
            var config = new AmazonDynamoDBConfig
            {
                ServiceURL = regionOrEndpoint
            };
            _client = new AmazonDynamoDBClient("test", "test", config);
        }
        else
        {
            var region = RegionEndpoint.GetBySystemName(regionOrEndpoint);
            _client = new AmazonDynamoDBClient(region);
        }
    }

    public DynamoDbPersistenceProvider(IAmazonDynamoDB client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        try
        {
            await _client.DescribeTableAsync(TableName, ct);
            return; // table already exists
        }
        catch (ResourceNotFoundException)
        {
            // Need to create
        }

        var createRequest = new CreateTableRequest
        {
            TableName = TableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = new List<AttributeDefinition>
            {
                new() { AttributeName = "BatchId", AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "Version", AttributeType = ScalarAttributeType.S }
            },
            KeySchema = new List<KeySchemaElement>
            {
                new() { AttributeName = "BatchId", KeyType = KeyType.HASH },
                new() { AttributeName = "Version", KeyType = KeyType.RANGE }
            },
            GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
            {
                new()
                {
                    IndexName = GsiName,
                    KeySchema = new List<KeySchemaElement>
                    {
                        new() { AttributeName = "Version", KeyType = KeyType.HASH }
                    },
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                }
            }
        };

        await _client.CreateTableAsync(createRequest, ct);

        // Wait for table to become ACTIVE
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var desc = await _client.DescribeTableAsync(TableName, ct);
            if (desc.Table.TableStatus == TableStatus.ACTIVE)
                return;

            await Task.Delay(1000, ct);
        }

        throw new TimeoutException($"DynamoDB table '{TableName}' did not become ACTIVE within 60 seconds.");
    }

    public async Task<bool> IsExecutedAsync(string version, CancellationToken ct = default)
    {
        var request = new QueryRequest
        {
            TableName = TableName,
            IndexName = GsiName,
            KeyConditionExpression = "Version = :v",
            FilterExpression = "Success = :s",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":v"] = new AttributeValue { S = version },
                [":s"] = new AttributeValue { BOOL = true }
            },
            Limit = 1
        };

        var response = await _client.QueryAsync(request, ct);
        return response.Count > 0;
    }

    public async Task RecordExecutionAsync(WorkItemRecord record, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["BatchId"] = new() { S = record.BatchId },
            ["Version"] = new() { S = record.Version },
            ["ExecutedAt"] = new() { S = record.ExecutedAt.ToString("O") },
            ["AssemblyQualifiedName"] = new() { S = record.AssemblyQualifiedName },
            ["Success"] = new() { BOOL = record.Success }
        };

        if (record.ErrorMessage != null)
            item["ErrorMessage"] = new AttributeValue { S = record.ErrorMessage };

        await _client.PutItemAsync(new PutItemRequest
        {
            TableName = TableName,
            Item = item
        }, ct);
    }

    public async Task RemoveExecutionAsync(string version, CancellationToken ct = default)
    {
        // Need to find the BatchId first via GSI
        var queryRequest = new QueryRequest
        {
            TableName = TableName,
            IndexName = GsiName,
            KeyConditionExpression = "Version = :v",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":v"] = new AttributeValue { S = version }
            }
        };

        var queryResponse = await _client.QueryAsync(queryRequest, ct);

        foreach (var item in queryResponse.Items)
        {
            if (!item.TryGetValue("BatchId", out var batchIdAttr))
                continue;

            await _client.DeleteItemAsync(new DeleteItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["BatchId"] = new() { S = batchIdAttr.S },
                    ["Version"] = new() { S = version }
                }
            }, ct);
        }
    }

    public async Task<IEnumerable<WorkItemRecord>> GetBatchAsync(string batchId, CancellationToken ct = default)
    {
        var request = new QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "BatchId = :b",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":b"] = new AttributeValue { S = batchId }
            }
        };

        var response = await _client.QueryAsync(request, ct);
        return response.Items.Select(MapRecord);
    }

    public async Task<IEnumerable<BatchSummary>> GetAllBatchesAsync(CancellationToken ct = default)
    {
        // Scan entire table, group by BatchId
        var request = new ScanRequest { TableName = TableName };
        var allItems = new List<Dictionary<string, AttributeValue>>();

        ScanResponse? response;
        do
        {
            response = await _client.ScanAsync(request, ct);
            allItems.AddRange(response.Items);
            request.ExclusiveStartKey = response.LastEvaluatedKey;
        }
        while (response.LastEvaluatedKey?.Count > 0);

        return allItems
            .GroupBy(item => item["BatchId"].S)
            .Select(g => new BatchSummary
            {
                BatchId = g.Key,
                StartedAt = g.Min(item =>
                    DateTimeOffset.Parse(item["ExecutedAt"].S)),
                ItemCount = g.Count()
            })
            .OrderByDescending(b => b.StartedAt)
            .ToList();
    }

    private static WorkItemRecord MapRecord(Dictionary<string, AttributeValue> item)
    {
        return new WorkItemRecord
        {
            Version = item["Version"].S,
            BatchId = item["BatchId"].S,
            ExecutedAt = DateTimeOffset.Parse(item["ExecutedAt"].S),
            AssemblyQualifiedName = item["AssemblyQualifiedName"].S,
            Success = item["Success"].BOOL,
            ErrorMessage = item.TryGetValue("ErrorMessage", out var err) ? err.S : null
        };
    }
}
