using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using RunOnce.Abstractions;

namespace RunOnce.Persistence.SqlServer;

public class SqlServerPersistenceProvider : IPersistenceProvider
{
    private readonly string _connectionString;

    private const string TableName = "__RunOnceHistory";

    public SqlServerPersistenceProvider(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const string sql = $"""
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = '__RunOnceHistory'
            )
            BEGIN
                CREATE TABLE __RunOnceHistory (
                    Version               NVARCHAR(50)   NOT NULL PRIMARY KEY,
                    BatchId               NVARCHAR(50)   NOT NULL,
                    ExecutedAt            DATETIMEOFFSET NOT NULL,
                    AssemblyQualifiedName NVARCHAR(500)  NOT NULL,
                    Success               BIT            NOT NULL,
                    ErrorMessage          NVARCHAR(MAX)  NULL
                );
                CREATE INDEX IX_RunOnceHistory_BatchId ON __RunOnceHistory(BatchId);
            END
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> IsExecutedAsync(string version, CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT COUNT(1) FROM __RunOnceHistory
            WHERE Version = @Version AND Success = 1
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Version", version);
        var count = (int)await cmd.ExecuteScalarAsync(ct);
        return count > 0;
    }

    public async Task RecordExecutionAsync(WorkItemRecord record, CancellationToken ct = default)
    {
        const string sql = """
            MERGE __RunOnceHistory AS target
            USING (SELECT @Version AS Version) AS source ON target.Version = source.Version
            WHEN MATCHED THEN
                UPDATE SET
                    BatchId = @BatchId,
                    ExecutedAt = @ExecutedAt,
                    AssemblyQualifiedName = @AssemblyQualifiedName,
                    Success = @Success,
                    ErrorMessage = @ErrorMessage
            WHEN NOT MATCHED THEN
                INSERT (Version, BatchId, ExecutedAt, AssemblyQualifiedName, Success, ErrorMessage)
                VALUES (@Version, @BatchId, @ExecutedAt, @AssemblyQualifiedName, @Success, @ErrorMessage);
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Version", record.Version);
        cmd.Parameters.AddWithValue("@BatchId", record.BatchId);
        cmd.Parameters.AddWithValue("@ExecutedAt", record.ExecutedAt);
        cmd.Parameters.AddWithValue("@AssemblyQualifiedName", record.AssemblyQualifiedName);
        cmd.Parameters.AddWithValue("@Success", record.Success);
        cmd.Parameters.AddWithValue("@ErrorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveExecutionAsync(string version, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM __RunOnceHistory WHERE Version = @Version";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Version", version);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IEnumerable<WorkItemRecord>> GetBatchAsync(string batchId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT Version, BatchId, ExecutedAt, AssemblyQualifiedName, Success, ErrorMessage
            FROM __RunOnceHistory
            WHERE BatchId = @BatchId
            """;

        var results = new List<WorkItemRecord>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BatchId", batchId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapRecord(reader));
        }
        return results;
    }

    public async Task<IEnumerable<BatchSummary>> GetAllBatchesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT BatchId, MIN(ExecutedAt) AS StartedAt, COUNT(*) AS ItemCount
            FROM __RunOnceHistory
            GROUP BY BatchId
            ORDER BY MIN(ExecutedAt) DESC
            """;

        var results = new List<BatchSummary>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new BatchSummary
            {
                BatchId = reader.GetString(0),
                StartedAt = reader.GetDateTimeOffset(1),
                ItemCount = reader.GetInt32(2)
            });
        }
        return results;
    }

    private static WorkItemRecord MapRecord(SqlDataReader reader)
    {
        return new WorkItemRecord
        {
            Version = reader.GetString(0),
            BatchId = reader.GetString(1),
            ExecutedAt = reader.GetDateTimeOffset(2),
            AssemblyQualifiedName = reader.GetString(3),
            Success = reader.GetBoolean(4),
            ErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5)
        };
    }
}
