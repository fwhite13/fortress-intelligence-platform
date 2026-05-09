# ADO#3116 — Fix: Add MigrateAsync before seeding in Program.cs

## Problem
In `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Program.cs`, there is NO call to `MigrateAsync()` at all. The seeding block (starting around line 211) runs immediately after `var app = builder.Build();` and directly queries the database (e.g. `seedDb.McpServers.FirstOrDefaultAsync(...)`) — but migrations have never been applied first. On first launch after schema changes, the tables don't exist yet, causing an exit code 139 crash.

## Fix Required
Add a `MigrateAsync()` call immediately before the seeding block. The migration must happen before ANY database read or write operations.

### Exact location to insert
In `Program.cs`, find this line:
```csharp
var app = builder.Build();

// Seed mcp_servers with all MCP tool groups (idempotent)
using (var seedScope = app.Services.CreateScope())
```

### What to insert between `builder.Build()` and the seed scope block:
```csharp
// Run EF Core migrations before any seeding or DB access
using (var migrateScope = app.Services.CreateScope())
{
    var migrateLogger = migrateScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var dbFactory = migrateScope.ServiceProvider.GetRequiredService<IDbContextFactory<FaitV2DbContext>>();
        await using var migrateDb = await dbFactory.CreateDbContextAsync();
        migrateLogger.LogInformation("Running EF Core migrations...");
        await migrateDb.Database.MigrateAsync();
        migrateLogger.LogInformation("EF Core migrations complete.");
    }
    catch (Exception ex)
    {
        migrateLogger.LogError(ex, "EF Core migration failed. Startup aborted.");
        throw;
    }
}
```

## Constraints
- Insert ONLY the migration block above. Do NOT modify any other code.
- The migration block must appear AFTER `var app = builder.Build();` and BEFORE the `// Seed mcp_servers` comment and seeding scope block.
- `MigrateAsync` errors must be logged with `LogError` and then rethrown (do NOT swallow).
- The existing seeding code block (using `seedScope`) must remain 100% unchanged.
- Do NOT add any other changes — no formatting, no refactoring, nothing else.

## File to edit
`/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Program.cs`
