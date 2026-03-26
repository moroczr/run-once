# Guide: Using RunOnce in an Application

This guide walks through building a solution where work items run automatically when your application starts, using RunOnce integrated into the .NET dependency injection container.

**What you'll build:**

```
MyApp.sln
├── src/
│   ├── MyApp/                   ASP.NET Core web app (or any .NET host)
│   └── MyApp.Migrations/        Class library — work items live here
```

---

## Step 1 — Create the solution

```sh
mkdir MyApp && cd MyApp
dotnet new sln -n MyApp
```

---

## Step 2 — Create the migrations project

The migrations project only needs `RunOnce.Abstractions`. It has no dependency on the host or any persistence package.

```sh
dotnet new classlib -n MyApp.Migrations -o src/MyApp.Migrations
dotnet sln add src/MyApp.Migrations/MyApp.Migrations.csproj
dotnet add src/MyApp.Migrations package RunOnce.Abstractions
```

Delete the default `Class1.cs`:

```sh
rm src/MyApp.Migrations/Class1.cs
```

---

## Step 3 — Write a Startup class

Create `src/MyApp.Migrations/Startup.cs`. This is where you register services that your work items need via constructor injection. If your work items have no dependencies beyond what the host already provides, you can skip this file entirely.

```csharp
using Microsoft.Extensions.DependencyInjection;
using RunOnce.Abstractions;

namespace MyApp.Migrations;

// Example service used by work items
public interface ISchemaLogger
{
    void Log(string message);
}

public class ConsoleSchemaLogger : ISchemaLogger
{
    public void Log(string message) => Console.WriteLine($"[schema] {message}");
}

// Discovered automatically — no manual registration needed
public class Startup : IRunOnceStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISchemaLogger, ConsoleSchemaLogger>();
    }
}
```

---

## Step 4 — Write work items

Create a `WorkItems/` folder inside `src/MyApp.Migrations/`. Each file is one work item.

**`WorkItems/CreateUsersTable.cs`**

```csharp
using Microsoft.Data.SqlClient;
using RunOnce.Abstractions;

namespace MyApp.Migrations.WorkItems;

[Version("20240101000000")]
public class CreateUsersTable : IWorkItem
{
    private readonly ISchemaLogger _log;
    private readonly string _connectionString;

    public CreateUsersTable(ISchemaLogger log, IConfiguration config)
    {
        _log = log;
        _connectionString = config.GetConnectionString("Default")!;
    }

    public async Task UpAsync(CancellationToken ct = default)
    {
        _log.Log("Creating Users table...");
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users')
            CREATE TABLE Users (
                Id        INT IDENTITY PRIMARY KEY,
                Email     NVARCHAR(256) NOT NULL,
                CreatedAt DATETIME2     NOT NULL DEFAULT GETUTCDATE()
            )
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DownAsync(CancellationToken ct = default)
    {
        _log.Log("Dropping Users table...");
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DROP TABLE IF EXISTS Users";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
```

**`WorkItems/SeedAdminUser.cs`**

```csharp
using Microsoft.Data.SqlClient;
using RunOnce.Abstractions;

namespace MyApp.Migrations.WorkItems;

[Version("20240102000000")]
[Tags("seed")]
public class SeedAdminUser : IWorkItem
{
    private readonly ISchemaLogger _log;
    private readonly string _connectionString;

    public SeedAdminUser(ISchemaLogger log, IConfiguration config)
    {
        _log = log;
        _connectionString = config.GetConnectionString("Default")!;
    }

    public async Task UpAsync(CancellationToken ct = default)
    {
        _log.Log("Seeding admin user...");
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'admin@example.com')
            INSERT INTO Users (Email) VALUES ('admin@example.com')
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DownAsync(CancellationToken ct = default)
    {
        _log.Log("Removing admin user...");
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Users WHERE Email = 'admin@example.com'";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
```

Your migrations project structure now looks like:

```
src/MyApp.Migrations/
├── Startup.cs
└── WorkItems/
    ├── CreateUsersTable.cs
    └── SeedAdminUser.cs
```

---

## Step 5 — Create the host application

```sh
dotnet new web -n MyApp -o src/MyApp
dotnet sln add src/MyApp/MyApp.csproj
```

Add a reference to the migrations project and the RunOnce packages:

```sh
dotnet add src/MyApp reference src/MyApp.Migrations/MyApp.Migrations.csproj
dotnet add src/MyApp package RunOnce.Core
dotnet add src/MyApp package RunOnce.Persistence.SqlServer
```

---

## Step 6 — Register RunOnce in the host

Open `src/MyApp/Program.cs` and add RunOnce to the service container. The typical pattern is to run migrations at startup before the application begins serving traffic.

```csharp
using RunOnce.Core.DependencyInjection;
using RunOnce.Core.Discovery;
using RunOnce.Core.Execution;
using MyApp.Migrations;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;

// Register RunOnce — scans MyApp.Migrations for work items and the Startup class
builder.Services.AddRunOnce(options => options
    .UseAssembly(typeof(MyApp.Migrations.Startup).Assembly)
    .UseSqlServer(connectionString));

var app = builder.Build();

// Run migrations before accepting traffic
await using (var scope = app.Services.CreateAsyncScope())
{
    var executor = scope.ServiceProvider.GetRequiredService<RunOnceExecutor>();
    var discoverer = new WorkItemDiscoverer();
    var descriptors = discoverer.Discover(new[] { typeof(MyApp.Migrations.Startup).Assembly });

    var result = await executor.UpAsync(descriptors);

    app.Logger.LogInformation(
        "RunOnce: executed={Executed} skipped={Skipped} failed={Failed} batch={Batch}",
        result.ExecutedVersions.Count,
        result.SkippedVersions.Count,
        result.FailedVersions.Count,
        result.BatchId);

    if (result.FailedVersions.Count > 0)
        throw new Exception($"RunOnce: {result.FailedVersions.Count} work item(s) failed. Aborting startup.");
}

app.MapGet("/", () => "Hello World!");

app.Run();
```

Add a connection string to `src/MyApp/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=MyApp;Integrated Security=true;TrustServerCertificate=true"
  }
}
```

---

## Step 7 — Run the application

```sh
dotnet run --project src/MyApp
```

On first start you will see log output similar to:

```
info: RunOnce: executed=2 skipped=0 failed=0 batch=20240115093000-a1b2c3d4e5f6g7
```

On every subsequent start, all items are already recorded as executed and are skipped:

```
info: RunOnce: executed=0 skipped=2 failed=0 batch=20240115094500-b2c3d4e5f6g7h8
```

---

## Step 8 — Add a new work item

When you need a new migration, add a file with the next version timestamp. No other changes are needed — RunOnce discovers it automatically on the next startup.

```csharp
// WorkItems/AddEmailIndex.cs
using RunOnce.Abstractions;

namespace MyApp.Migrations.WorkItems;

[Version("20240110000000")]
[Tags("perf")]
public class AddEmailIndex : IWorkItem
{
    public async Task UpAsync(CancellationToken ct = default)
    {
        // ...
    }

    public async Task DownAsync(CancellationToken ct = default)
    {
        // ...
    }
}
```

---

## Step 9 — Rolling back (optional)

If you need to reverse a batch, use the batch ID logged at startup. You can call `DownAsync` directly from code:

```csharp
var executor = serviceProvider.GetRequiredService<RunOnceExecutor>();
var discoverer = new WorkItemDiscoverer();
var descriptors = discoverer.Discover(new[] { typeof(MyApp.Migrations.Startup).Assembly });

await executor.DownAsync("20240115093000-a1b2c3d4e5f6g7", descriptors);
```

Or use the CLI against the same database (see [guide-cli.md](guide-cli.md)):

```sh
run-once down \
  --assembly src/MyApp.Migrations/bin/Release/net8.0/MyApp.Migrations.dll \
  --provider sql \
  --connection-string "Server=localhost;Database=MyApp;Integrated Security=true;TrustServerCertificate=true" \
  --batch 20240115093000-a1b2c3d4e5f6g7
```

---

## Summary

| Step | What you did |
|------|-------------|
| 1–2 | Created the solution and a dedicated migrations class library. |
| 3 | Added an `IRunOnceStartup` class to register services work items depend on. |
| 4 | Wrote two work items with versioned, ordered logic. |
| 5 | Created the host app and added project + package references. |
| 6 | Registered RunOnce in DI and called `UpAsync` at startup before serving traffic. |
| 7–8 | Verified idempotent execution and saw how to add new work items. |
| 9 | Saw how to roll back a batch when needed. |
