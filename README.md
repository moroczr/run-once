# RunOnce

A FluentMigrator-style framework for running versioned, ordered work items — not just SQL migrations. Each work item runs exactly once, is tracked in a persistence store, and can be rolled back by batch.

---

## Table of Contents

- [Concepts](#concepts)
- [Packages](#packages)
- [Quick Start](#quick-start)
- [Defining Work Items](#defining-work-items)
  - [The IWorkItem Interface](#the-iworkitem-interface)
  - [Versioning](#versioning)
  - [Tagging](#tagging)
- [Configuring DI (class library / hosted app)](#configuring-di-class-library--hosted-app)
  - [SQL Server](#sql-server)
  - [DynamoDB](#dynamodb)
  - [Running via IServiceProvider](#running-via-iserviceprovider)
- [Startup Class (custom DI registrations)](#startup-class-custom-di-registrations)
- [CLI Tool](#cli-tool)
  - [Installation](#installation)
  - [Common Flags](#common-flags)
  - [up — execute pending work items](#up--execute-pending-work-items)
  - [down — rollback a batch](#down--rollback-a-batch)
  - [list — show executed history](#list--show-executed-history)
  - [list-batches — show batch summary](#list-batches--show-batch-summary)
  - [Tag Filtering via CLI](#tag-filtering-via-cli)
  - [Custom Persistence Provider via CLI](#custom-persistence-provider-via-cli)
- [Persistence Providers](#persistence-providers)
  - [SQL Server](#sql-server-1)
  - [DynamoDB](#dynamodb-1)
  - [Implementing a Custom Provider](#implementing-a-custom-provider)
- [Execution Behaviour](#execution-behaviour)
- [Error Handling](#error-handling)
- [Exit Codes](#exit-codes)

---

## Concepts

| Term | Description |
|------|-------------|
| **Work item** | A class implementing `IWorkItem` with `UpAsync` / `DownAsync` methods. |
| **Version** | A string identifying the work item's order (e.g. a timestamp). Must be unique across all work items. |
| **Batch** | All work items executed in a single `up` run share a batch ID. Used to target rollbacks. |
| **Persistence provider** | Stores the execution history (which versions ran, in which batch, success/failure). |

---

## Packages

| Package | Use |
|---------|-----|
| `RunOnce.Abstractions` | `IWorkItem`, `[Version]`, `[Tags]`, `IRunOnceStartup` — everything needed to define work items. |
| `RunOnce.Core` | The executor, discoverer, and `AddRunOnce` DI extension — needed wherever you run migrations. |
| `RunOnce.Persistence.SqlServer` | SQL Server persistence provider. |
| `RunOnce.Persistence.DynamoDb` | DynamoDB persistence provider. |
| `RunOnce` *(tool)* | The `run-once` CLI — no project reference needed. Bundles both persistence providers. |

You can put work items and wiring code in the same project — just install all the packages you need there. The typical reason to split them is when you want to run the migrations assembly via the CLI (which bundles its own providers) while also referencing it from a host app, and you don't want the persistence packages pulled into places that don't need them. For most projects, a single project is fine.

---

## Quick Start

1. Create a class library for your work items and install the abstractions package:

```sh
dotnet new classlib -n MyApp.Migrations
cd MyApp.Migrations
dotnet add package RunOnce.Abstractions
```

2. Write a work item:

```csharp
using RunOnce.Abstractions;

[Version("20240101000000")]
public class CreateUsersTable : IWorkItem
{
    public Task UpAsync(CancellationToken ct = default)
    {
        // your logic here
        return Task.CompletedTask;
    }

    public Task DownAsync(CancellationToken ct = default)
    {
        // reverse logic here
        return Task.CompletedTask;
    }
}
```

3. Install and run the CLI:

```sh
dotnet tool install -g RunOnce
run-once up \
  --assembly ./bin/Release/net8.0/MyApp.Migrations.dll \
  --provider sql \
  --connection-string "Server=localhost;Database=MyApp;Integrated Security=true;TrustServerCertificate=true"
```

---

## Defining Work Items

### The IWorkItem Interface

```csharp
public interface IWorkItem
{
    Task UpAsync(CancellationToken cancellationToken = default);
    Task DownAsync(CancellationToken cancellationToken = default);
}
```

- `UpAsync` — forward migration. Called when the work item has not yet been recorded as successfully executed.
- `DownAsync` — reverse migration. Called during a `down` (rollback) operation for the batch the item belongs to.

Work items are resolved via the DI container, so constructor injection works for any service registered in your `Startup` class.

### Versioning

Every work item must have exactly one `[Version]` attribute. The version string determines execution order — items are sorted using ordinal string comparison, so zero-padded timestamps work well:

```csharp
[Version("20240115093000")]   // 2024-01-15 09:30:00
public class AddOrdersTable : IWorkItem { ... }
```

Rules:
- Versions must be unique. Duplicates across any loaded assembly throw an `InvalidOperationException` at discovery time.
- Classes implementing `IWorkItem` without a `[Version]` attribute are silently ignored by the discoverer.

### Tagging

Tags let you run a subset of work items. Apply them with `[Tags]`:

```csharp
[Version("20240102000000")]
[Tags("seed")]
public class SeedAdminUser : IWorkItem { ... }

[Version("20240103000000")]
[Tags("perf")]
public class AddEmailIndex : IWorkItem { ... }

[Version("20240104000000")]
[Tags("seed", "data")]
public class SeedProductCatalog : IWorkItem { ... }
```

When a tag filter is active:
- Items matching **at least one** of the requested tags are included.
- **Untagged items always run**, regardless of the filter.
- Tag comparison is **case-insensitive**.

---

## Configuring DI (class library / hosted app)

Reference both `RunOnce.Core` and your chosen persistence package:

```sh
dotnet add package RunOnce.Core
dotnet add package RunOnce.Persistence.SqlServer   # or DynamoDb
```

Register RunOnce in your `IServiceCollection`:

### SQL Server

```csharp
using RunOnce.Core.DependencyInjection;
using RunOnce.Persistence.SqlServer;

services.AddRunOnce(options => options
    .UseAssembly(typeof(CreateUsersTable).Assembly)
    .UseSqlServer(connectionString));
```

### DynamoDB

Pass either an AWS region name or a full HTTP endpoint (for LocalStack):

```csharp
using RunOnce.Core.DependencyInjection;
using RunOnce.Persistence.DynamoDb;

// Real AWS
services.AddRunOnce(options => options
    .UseAssembly(typeof(CreateUsersTable).Assembly)
    .UseDynamoDb("us-east-1"));

// LocalStack
services.AddRunOnce(options => options
    .UseAssembly(typeof(CreateUsersTable).Assembly)
    .UseDynamoDb("http://localhost:4566"));
```

`AddRunOnce` will:
1. Register the persistence provider as a singleton.
2. Scan the assembly for any `IRunOnceStartup` implementation and call `ConfigureServices`.
3. Register all `IWorkItem` implementations as transient services.
4. Register `RunOnceExecutor` as a transient service.

`.UseAssembly(...)` is optional — omit it if you only want to wire up the executor without scanning a specific assembly (e.g. when passing descriptors manually).

### Running via IServiceProvider

```csharp
using RunOnce.Core.Discovery;
using RunOnce.Core.Execution;

var executor = serviceProvider.GetRequiredService<RunOnceExecutor>();
var discoverer = new WorkItemDiscoverer();
var descriptors = discoverer.Discover(new[] { typeof(CreateUsersTable).Assembly });

UpResult result = await executor.UpAsync(descriptors);

Console.WriteLine($"Batch:    {result.BatchId}");
Console.WriteLine($"Executed: {result.ExecutedVersions.Count}");
Console.WriteLine($"Skipped:  {result.SkippedVersions.Count}");
Console.WriteLine($"Failed:   {result.FailedVersions.Count}");
```

With tag filtering:

```csharp
var descriptors = discoverer.Discover(
    new[] { typeof(CreateUsersTable).Assembly },
    tags: new[] { "seed" });
```

With `ContinueOnFailure`:

```csharp
var result = await executor.UpAsync(descriptors, new UpOptions
{
    ContinueOnFailure = true   // log failures and keep going instead of throwing
});
```

Rolling back a batch:

```csharp
await executor.DownAsync(batchId, descriptors);
```

---

## Startup Class (custom DI registrations)

If your work items depend on application-specific services, create a class in your migrations assembly that implements `IRunOnceStartup`. It is discovered automatically — no manual registration is needed.

```csharp
using Microsoft.Extensions.DependencyInjection;
using RunOnce.Abstractions;

public class Startup : IRunOnceStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMyMigrationService, MyMigrationService>();
    }
}
```

Your work items can then receive `IMyMigrationService` via constructor injection:

```csharp
[Version("20240105000000")]
public class MigrateUserData : IWorkItem
{
    private readonly IMyMigrationService _svc;

    public MigrateUserData(IMyMigrationService svc) => _svc = svc;

    public async Task UpAsync(CancellationToken ct = default)
    {
        await _svc.MigrateAsync(ct);
    }

    public Task DownAsync(CancellationToken ct = default) => Task.CompletedTask;
}
```

Notes:
- Only one `IRunOnceStartup` implementation is supported per scanned assembly set. If multiple are found, the last one discovered wins.
- `IRunOnceStartup` is called with a parameterless constructor — it cannot itself use DI.

---

## CLI Tool

The `run-once` CLI runs work items from a compiled assembly without requiring any code changes to your host application. All built-in persistence providers are bundled into the tool — no extra package installation is needed.

### Installation

**Global installation:**

```sh
dotnet tool install -g RunOnce
```

**Local installation (per-repo):**

```sh
dotnet tool install RunOnce
# invoked as: dotnet run-once ...
```

Verify the installation:

```sh
run-once --version
```

### Common Flags

These flags are shared across `up`, `down`, `list`, and `list-batches`:

| Flag | Required | Description |
|------|----------|-------------|
| `--connection-string <value>` | Yes | Connection string for SQL Server, or region/endpoint for DynamoDB. |
| `--provider <name>` | Conditionally | Built-in provider: `sql` (or `sqlserver`) / `dynamo` (or `dynamodb`). Required unless `--provider-assembly` is used. |
| `--provider-assembly <path>` | Conditionally | Path to a DLL containing a custom `IPersistenceProvider`. Alternative to `--provider`. |

The `up` and `down` commands also accept:

| Flag | Description |
|------|-------------|
| `--assembly <path>` | Path to a single DLL containing work items. |
| `--directory <path>` | Path to a directory — all `.dll` files are scanned for work items. |

At least one of `--assembly` or `--directory` must be provided for `up` and `down`.

---

### up — execute pending work items

Runs all pending work items from the specified assembly in version order. Already-executed items (recorded with `Success=true`) are skipped. Returns exit code `0` on full success, `1` if any item failed.

```sh
run-once up \
  --assembly ./MyApp.Migrations.dll \
  --provider sql \
  --connection-string "Server=.;Database=MyApp;Integrated Security=true;TrustServerCertificate=true"
```

Example output:

```
Batch: 20240115093000-a1b2c3d4e5f6g7
Executed : 3
Skipped  : 1
Failed   : 0
  [OK]      20240101000000
  [OK]      20240102000000
  [OK]      20240103000000
  [SKIP]    20231201000000
```

**Options specific to `up`:**

| Flag | Default | Description |
|------|---------|-------------|
| `--tags <tag> [<tag> ...]` | *(all items)* | Only run items matching at least one tag. Untagged items always run. |
| `--continue-on-failure` | `false` | Log failures and continue instead of halting on the first failure. |

---

### down — rollback a batch

Reverses all successfully-executed items in a given batch by calling `DownAsync` in **reverse version order**, then removes those records from the persistence store, making the items eligible to run again.

```sh
run-once down \
  --assembly ./MyApp.Migrations.dll \
  --provider sql \
  --connection-string "..." \
  --batch 20240115093000-a1b2c3d4e5f6g7
```

The `--batch` flag is required. Obtain the batch ID from the `up` output or from `list-batches`.

---

### list — show executed history

Lists all executed work items, grouped by batch, sorted by version within each batch:

```sh
run-once list \
  --provider sql \
  --connection-string "..."
```

Example output:

```
[OK]     20240101000000                 batch=20240115093000-a1b2c3d4  at=2024-01-15 09:30:01Z
[OK]     20240102000000                 batch=20240115093000-a1b2c3d4  at=2024-01-15 09:30:02Z
[FAILED] 20240103000000                 batch=20240115093000-a1b2c3d4  at=2024-01-15 09:30:03Z
         Error: Column 'Email' already exists.
```

---

### list-batches — show batch summary

Lists all batches with their start time and item count, most recent first:

```sh
run-once list-batches \
  --provider sql \
  --connection-string "..."
```

Example output:

```
BatchId                        StartedAt                 Items
-----------------------------------------------------------------
20240115093000-a1b2c3d4e5f6g7  2024-01-15 09:30:00Z          3
20231201120000-z9y8x7w6v5u4t3  2023-12-01 12:00:00Z          5
```

---

### Tag Filtering via CLI

Pass one or more tags to `--tags` to restrict which work items run. Untagged items always run regardless of the filter.

```sh
# Run only items tagged "seed" (plus all untagged items)
run-once up \
  --assembly ./MyApp.Migrations.dll \
  --provider sql \
  --connection-string "..." \
  --tags seed

# Run items tagged "seed" OR "perf"
run-once up \
  --assembly ./MyApp.Migrations.dll \
  --provider sql \
  --connection-string "..." \
  --tags seed perf
```

---

### Custom Persistence Provider via CLI

If you have implemented your own `IPersistenceProvider`, point the CLI at its DLL:

```sh
run-once up \
  --assembly ./MyApp.Migrations.dll \
  --provider-assembly ./MyApp.Persistence.Custom.dll \
  --connection-string "my-custom-connection-string"
```

The CLI will scan the assembly for the first concrete type implementing `IPersistenceProvider` and instantiate it. It tries a `(string)` constructor first (passing `--connection-string`), then a parameterless constructor.

---

## Persistence Providers

### SQL Server

**Package:** `RunOnce.Persistence.SqlServer`
**CLI flag:** `--provider sql` (or `--provider sqlserver`)

Creates and manages a single table named `__RunOnceHistory` in the target database. The table is created automatically on the first run if it does not exist — no manual schema setup is required.

```
__RunOnceHistory
  Version               NVARCHAR(50)   PRIMARY KEY
  BatchId               NVARCHAR(50)
  ExecutedAt            DATETIMEOFFSET
  AssemblyQualifiedName NVARCHAR(500)
  Success               BIT
  ErrorMessage          NVARCHAR(MAX)  NULL
```

An index on `BatchId` (`IX_RunOnceHistory_BatchId`) is created alongside the table.

**DI setup:**

```csharp
services.AddRunOnce(options => options
    .UseAssembly(typeof(MyWorkItem).Assembly)
    .UseSqlServer("Server=.;Database=MyApp;Integrated Security=true;TrustServerCertificate=true"));
```

Or construct directly:

```csharp
var provider = new SqlServerPersistenceProvider(connectionString);
```

---

### DynamoDB

**Package:** `RunOnce.Persistence.DynamoDb`
**CLI flag:** `--provider dynamo` (or `--provider dynamodb`)

Creates and manages a table named `RunOnceHistory` with the following schema:

- **Partition key:** `BatchId` (String)
- **Sort key:** `Version` (String)
- **GSI:** `Version-index` — partition key `Version` (used for duplicate-check lookups)
- **Billing mode:** `PAY_PER_REQUEST`

The table is created automatically on the first run if it does not exist.

**DI setup (real AWS):**

```csharp
services.AddRunOnce(options => options
    .UseAssembly(typeof(MyWorkItem).Assembly)
    .UseDynamoDb("us-east-1"));
```

**DI setup (LocalStack or custom endpoint):**

```csharp
services.AddRunOnce(options => options
    .UseAssembly(typeof(MyWorkItem).Assembly)
    .UseDynamoDb("http://localhost:4566"));
```

**Providing your own `IAmazonDynamoDB` client:**

```csharp
var dynamoClient = new AmazonDynamoDBClient(...);
var provider = new DynamoDbPersistenceProvider(dynamoClient);

services.AddRunOnce(options => options
    .UseAssembly(typeof(MyWorkItem).Assembly)
    .UseProvider(provider));
```

**CLI with DynamoDB:**

```sh
# Real AWS region
run-once up \
  --assembly ./MyApp.Migrations.dll \
  --provider dynamo \
  --connection-string us-east-1

# LocalStack
run-once up \
  --assembly ./MyApp.Migrations.dll \
  --provider dynamo \
  --connection-string http://localhost:4566
```

---

### Implementing a Custom Provider

Implement `IPersistenceProvider` from `RunOnce.Abstractions`:

```csharp
using RunOnce.Abstractions;

public class RedisProvider : IPersistenceProvider
{
    public RedisProvider(string connectionString) { ... }

    public Task EnsureSchemaAsync(CancellationToken ct = default) { ... }
    public Task<bool> IsExecutedAsync(string version, CancellationToken ct = default) { ... }
    public Task RecordExecutionAsync(WorkItemRecord record, CancellationToken ct = default) { ... }
    public Task RemoveExecutionAsync(string version, CancellationToken ct = default) { ... }
    public Task<IEnumerable<WorkItemRecord>> GetBatchAsync(string batchId, CancellationToken ct = default) { ... }
    public Task<IEnumerable<BatchSummary>> GetAllBatchesAsync(CancellationToken ct = default) { ... }
}
```

**Register in DI:**

```csharp
services.AddRunOnce(options => options
    .UseAssembly(typeof(MyWorkItem).Assembly)
    .UseProvider(new RedisProvider(connectionString)));
```

**Use with the CLI via `--provider-assembly`:**

```sh
run-once up \
  --assembly ./MyApp.Migrations.dll \
  --provider-assembly ./MyApp.Persistence.Redis.dll \
  --connection-string "redis://localhost:6379"
```

---

## Execution Behaviour

- Work items are executed in **ascending version order** (ordinal string sort). Using a fixed-width timestamp format (e.g. `yyyyMMddHHmmss`) guarantees chronological ordering.
- A work item is skipped if a record with `Success=true` already exists for its version in the persistence store. This check happens before each item, so it is safe to run `up` repeatedly — it is idempotent.
- Failed attempts are recorded with `Success=false`. A failed item is **not** considered executed and will be retried on the next `up` run.
- Each `up` run generates a new batch ID in the form `yyyyMMddHHmmss-<guid>` (26 characters). Items executed in a single run share this batch ID and can be rolled back together.
- `down` reverses items in **descending version order** within the batch, then removes their records, making them eligible to run again on the next `up`.
- `down` only reverses items that were recorded with `Success=true` in the target batch — failed records are left untouched.

---

## Error Handling

By default, the first work item failure stops execution and throws a `WorkItemExecutionException`. Pass `ContinueOnFailure = true` (or `--continue-on-failure` in the CLI) to log failures and continue processing remaining items.

```csharp
var result = await executor.UpAsync(descriptors, new UpOptions
{
    ContinueOnFailure = true
});

if (result.FailedVersions.Count > 0)
{
    // Handle failures
}
```

`UpResult` always contains:

| Property | Description |
|----------|-------------|
| `BatchId` | The batch ID for this run. |
| `ExecutedVersions` | Versions that ran and succeeded. |
| `SkippedVersions` | Versions already recorded as executed (skipped). |
| `FailedVersions` | Versions that threw an exception during this run. |

---

## Exit Codes

The CLI returns standard exit codes:

| Code | Meaning |
|------|---------|
| `0` | Success — all pending items executed, or nothing to do. |
| `1` | One or more items failed, or an unexpected error occurred. |
