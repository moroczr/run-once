# Guide: Using RunOnce with the CLI

This guide walks through building a standalone migrations project that is executed entirely via the `run-once` CLI — no host application required. This is the typical approach for CI/CD pipelines, deployment scripts, or any situation where you want to run migrations without embedding them in a running service.

**What you'll build:**

```
MyApp.sln
└── src/
    └── MyApp.Migrations/    Class library — work items live here
```

The CLI handles discovery, persistence, and execution. Your project only needs to contain work items.

---

## Step 1 — Install the CLI tool

**Global (available in any terminal):**

```sh
dotnet tool install -g RunOnce
```

**Local (per-repo, checked into source control):**

```sh
dotnet new tool-manifest   # creates .config/dotnet-tools.json
dotnet tool install RunOnce
```

With a local install, invoke it as `dotnet run-once` instead of `run-once`, or restore it in CI with:

```sh
dotnet tool restore
```

Verify the installation:

```sh
run-once --version
```

---

## Step 2 — Create the solution and migrations project

```sh
mkdir MyApp && cd MyApp
dotnet new sln -n MyApp
dotnet new classlib -n MyApp.Migrations -o src/MyApp.Migrations
dotnet sln add src/MyApp.Migrations/MyApp.Migrations.csproj
dotnet add src/MyApp.Migrations package RunOnce.Abstractions
```

Delete the default `Class1.cs`:

```sh
rm src/MyApp.Migrations/Class1.cs
```

---

## Step 3 — Write work items

Create a `WorkItems/` folder inside `src/MyApp.Migrations/`. Each file is one work item. Version strings must be unique and sort in execution order — a 14-digit timestamp (`yyyyMMddHHmmss`) works well.

**`WorkItems/CreateUsersTable.cs`**

```csharp
using RunOnce.Abstractions;

namespace MyApp.Migrations.WorkItems;

[Version("20240101000000")]
public class CreateUsersTable : IWorkItem
{
    public async Task UpAsync(CancellationToken ct = default)
    {
        Console.WriteLine("Creating Users table...");
        // Execute your SQL or other logic here
    }

    public async Task DownAsync(CancellationToken ct = default)
    {
        Console.WriteLine("Dropping Users table...");
        // Reverse logic here
    }
}
```

**`WorkItems/SeedAdminUser.cs`**

```csharp
using RunOnce.Abstractions;

namespace MyApp.Migrations.WorkItems;

[Version("20240102000000")]
[Tags("seed")]
public class SeedAdminUser : IWorkItem
{
    public async Task UpAsync(CancellationToken ct = default)
    {
        Console.WriteLine("Seeding admin user...");
    }

    public async Task DownAsync(CancellationToken ct = default)
    {
        Console.WriteLine("Removing admin user...");
    }
}
```

**`WorkItems/AddEmailIndex.cs`**

```csharp
using RunOnce.Abstractions;

namespace MyApp.Migrations.WorkItems;

[Version("20240103000000")]
[Tags("perf")]
public class AddEmailIndex : IWorkItem
{
    public async Task UpAsync(CancellationToken ct = default)
    {
        Console.WriteLine("Creating index on Users.Email...");
    }

    public async Task DownAsync(CancellationToken ct = default)
    {
        Console.WriteLine("Dropping index on Users.Email...");
    }
}
```

---

## Step 4 — Register custom dependencies (optional)

If your work items need injected services (e.g. a database connection, a logger, a config reader), add a `Startup.cs` to the project. The CLI discovers it automatically.

```csharp
using Microsoft.Extensions.DependencyInjection;
using RunOnce.Abstractions;

namespace MyApp.Migrations;

public interface ISchemaExecutor
{
    Task RunSqlAsync(string sql, CancellationToken ct = default);
}

public class SqlSchemaExecutor : ISchemaExecutor
{
    private readonly string _connectionString;

    public SqlSchemaExecutor(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task RunSqlAsync(string sql, CancellationToken ct = default)
    {
        // Execute sql against the database
    }
}

// Discovered and invoked automatically by the CLI
public class Startup : IRunOnceStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? throw new InvalidOperationException("CONNECTION_STRING env var is not set.");

        services.AddSingleton<ISchemaExecutor>(new SqlSchemaExecutor(connectionString));
    }
}
```

Work items can then take `ISchemaExecutor` in their constructor:

```csharp
[Version("20240101000000")]
public class CreateUsersTable : IWorkItem
{
    private readonly ISchemaExecutor _db;

    public CreateUsersTable(ISchemaExecutor db) => _db = db;

    public Task UpAsync(CancellationToken ct = default) =>
        _db.RunSqlAsync("CREATE TABLE Users (...)", ct);

    public Task DownAsync(CancellationToken ct = default) =>
        _db.RunSqlAsync("DROP TABLE IF EXISTS Users", ct);
}
```

> **Note:** `IRunOnceStartup` is instantiated with a parameterless constructor, so it cannot itself receive DI-injected services. Pull configuration from environment variables, files, or any other ambient source inside `ConfigureServices`.

---

## Step 5 — Build the project

The CLI loads your assembly from disk, so you need to build it first:

```sh
dotnet build src/MyApp.Migrations -c Release
```

The output assembly will be at:

```
src/MyApp.Migrations/bin/Release/net8.0/MyApp.Migrations.dll
```

---

## Step 6 — Run pending work items

```sh
run-once up \
  --assembly src/MyApp.Migrations/bin/Release/net8.0/MyApp.Migrations.dll \
  --provider sql \
  --connection-string "Server=localhost;Database=MyApp;Integrated Security=true;TrustServerCertificate=true"
```

Expected output on first run:

```
Batch: 20240115093000-a1b2c3d4e5f6g7
Executed : 3
Skipped  : 0
Failed   : 0
  [OK]      20240101000000
  [OK]      20240102000000
  [OK]      20240103000000
```

Running again immediately — all items already executed, nothing to do:

```
Batch: 20240115093100-b2c3d4e5f6g7h8
Executed : 0
Skipped  : 3
Failed   : 0
  [SKIP]    20240101000000
  [SKIP]    20240102000000
  [SKIP]    20240103000000
```

---

## Step 7 — Inspect execution history

**List all executed work items:**

```sh
run-once list \
  --provider sql \
  --connection-string "Server=localhost;Database=MyApp;Integrated Security=true;TrustServerCertificate=true"
```

```
[OK] 20240101000000                 batch=20240115093000-a1b2c3d4  at=2024-01-15 09:30:01Z
[OK] 20240102000000                 batch=20240115093000-a1b2c3d4  at=2024-01-15 09:30:02Z
[OK] 20240103000000                 batch=20240115093000-a1b2c3d4  at=2024-01-15 09:30:03Z
```

**List all batches:**

```sh
run-once list-batches \
  --provider sql \
  --connection-string "Server=localhost;Database=MyApp;Integrated Security=true;TrustServerCertificate=true"
```

```
BatchId                        StartedAt                 Items
-----------------------------------------------------------------
20240115093000-a1b2c3d4e5f6g7  2024-01-15 09:30:00Z          3
```

---

## Step 8 — Roll back a batch

Use the batch ID from the `up` output or from `list-batches`. `down` calls `DownAsync` on each item in the batch in reverse version order, then removes the records so the items can run again on the next `up`.

```sh
run-once down \
  --assembly src/MyApp.Migrations/bin/Release/net8.0/MyApp.Migrations.dll \
  --provider sql \
  --connection-string "Server=localhost;Database=MyApp;Integrated Security=true;TrustServerCertificate=true" \
  --batch 20240115093000-a1b2c3d4e5f6g7
```

```
Rollback of batch '20240115093000-a1b2c3d4e5f6g7' completed.
```

---

## Step 9 — Run a subset with tags

Run only `seed`-tagged items (plus all untagged items, which always run):

```sh
run-once up \
  --assembly src/MyApp.Migrations/bin/Release/net8.0/MyApp.Migrations.dll \
  --provider sql \
  --connection-string "..." \
  --tags seed
```

```
Batch: 20240115094000-c3d4e5f6g7h8i9
Executed : 2
Skipped  : 0
Failed   : 0
  [OK]      20240101000000    ← untagged, always runs
  [OK]      20240102000000    ← tagged "seed"
```

`20240103000000` (tagged `perf`) is not included because it does not match `seed`.

Run multiple tags at once (OR logic — any match is sufficient):

```sh
run-once up \
  --assembly src/MyApp.Migrations/bin/Release/net8.0/MyApp.Migrations.dll \
  --provider sql \
  --connection-string "..." \
  --tags seed perf
```

---

## Step 10 — Use with DynamoDB

Pass an AWS region name (or a LocalStack URL) as the connection string:

```sh
# Real AWS
run-once up \
  --assembly src/MyApp.Migrations/bin/Release/net8.0/MyApp.Migrations.dll \
  --provider dynamo \
  --connection-string us-east-1

# LocalStack
run-once up \
  --assembly src/MyApp.Migrations/bin/Release/net8.0/MyApp.Migrations.dll \
  --provider dynamo \
  --connection-string http://localhost:4566
```

---

## Step 11 — Integrate into CI/CD

A typical pipeline step after building your project:

```yaml
# GitHub Actions example
- name: Run database migrations
  env:
    CONNECTION_STRING: ${{ secrets.DB_CONNECTION_STRING }}
  run: |
    dotnet tool restore
    dotnet build src/MyApp.Migrations -c Release
    dotnet run-once up \
      --assembly src/MyApp.Migrations/bin/Release/net8.0/MyApp.Migrations.dll \
      --provider sql \
      --connection-string "$CONNECTION_STRING"
```

The command exits with code `0` on success and `1` if any work item fails, so the pipeline fails automatically on errors.

---

## Summary

| Step | What you did |
|------|-------------|
| 1 | Installed the `run-once` CLI globally or locally. |
| 2 | Created a solution with a single migrations class library referencing only `RunOnce.Abstractions`. |
| 3 | Wrote three work items with version timestamps and tags. |
| 4 | Optionally added a `Startup` class to register services needed by work items. |
| 5 | Built the project to produce the assembly the CLI loads. |
| 6 | Ran `run-once up` to execute pending items and saw idempotent behaviour. |
| 7 | Used `list` and `list-batches` to inspect history. |
| 8 | Rolled back a batch with `run-once down`. |
| 9 | Filtered execution to a subset of items using `--tags`. |
| 10 | Switched to DynamoDB by changing `--provider` and `--connection-string`. |
| 11 | Wired it into a CI/CD pipeline. |
