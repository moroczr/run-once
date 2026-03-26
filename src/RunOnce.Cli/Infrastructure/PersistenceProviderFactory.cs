using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RunOnce.Abstractions;
using RunOnce.Persistence.DynamoDb;
using RunOnce.Persistence.SqlServer;

namespace RunOnce.Cli.Infrastructure;

public static class PersistenceProviderFactory
{
    public static IPersistenceProvider Create(
        string? providerName,
        string? providerAssemblyPath,
        string connectionString)
    {
        if (providerAssemblyPath != null)
        {
            return LoadFromAssembly(providerAssemblyPath, connectionString);
        }

        return providerName?.ToLowerInvariant() switch
        {
            "sql" or "sqlserver" => new SqlServerPersistenceProvider(connectionString),
            "dynamo" or "dynamodb" => new DynamoDbPersistenceProvider(connectionString),
            _ => throw new InvalidOperationException(
                $"Unknown provider '{providerName}'. Use 'sql', 'dynamo', or --provider-assembly.")
        };
    }

    private static IPersistenceProvider LoadFromAssembly(string path, string connectionString)
    {
        var assembly = Assembly.LoadFrom(path);
        var providerType = assembly.GetTypes()
            .FirstOrDefault(t =>
                !t.IsAbstract &&
                !t.IsInterface &&
                typeof(IPersistenceProvider).IsAssignableFrom(t))
            ?? throw new InvalidOperationException(
                $"No type implementing IPersistenceProvider found in '{Path.GetFileName(path)}'.");

        // Try (string) constructor first, then parameterless
        var stringCtor = providerType.GetConstructor(new[] { typeof(string) });
        if (stringCtor != null)
            return (IPersistenceProvider)stringCtor.Invoke(new object[] { connectionString });

        var defaultCtor = providerType.GetConstructor(Type.EmptyTypes);
        if (defaultCtor != null)
            return (IPersistenceProvider)defaultCtor.Invoke(null);

        throw new InvalidOperationException(
            $"Type '{providerType.FullName}' has no suitable constructor (string) or ().");
    }
}
