# BUILD Task: ADO#2908 — FaitV2DbContextDesignTimeFactory env var fix

## File to modify
`src/FortressAI.V2.Web/Data/FaitV2DbContextDesignTimeFactory.cs`

## Current code (to be replaced)
```csharp
public FaitV2DbContext CreateDbContext(string[] args)
{
    var options = new DbContextOptionsBuilder<FaitV2DbContext>()
        .UseMySql(
            "Server=localhost;Database=fait_v2_dev;User=root;Password=dev;GuidFormat=None;",
            new MySqlServerVersion(new Version(8, 0, 28)))
        .Options;
    return new FaitV2DbContext(options);
}
```

## Replacement code
Replace the entire `CreateDbContext` method body with this implementation that reads environment variables with localhost fallbacks:

```csharp
public FaitV2DbContext CreateDbContext(string[] args)
{
    var host = Environment.GetEnvironmentVariable("FORTRESS_DB_HOST") ?? "localhost";
    var port = Environment.GetEnvironmentVariable("FORTRESS_DB_PORT") ?? "3306";
    var user = Environment.GetEnvironmentVariable("FORTRESS_DB_USER") ?? "root";
    var pass = Environment.GetEnvironmentVariable("FORTRESS_DB_PASS") ?? "dev";
    var db   = Environment.GetEnvironmentVariable("FORTRESS_DB_NAME") ?? "fait_v2_dev";

    var connStr = $"Server={host};Port={port};Database={db};User={user};Password={pass};GuidFormat=None;";

    var options = new DbContextOptionsBuilder<FaitV2DbContext>()
        .UseMySql(connStr, new MySqlServerVersion(new Version(8, 0, 28)))
        .Options;
    return new FaitV2DbContext(options);
}
```

## Instructions
1. Apply this exact change to `src/FortressAI.V2.Web/Data/FaitV2DbContextDesignTimeFactory.cs`
2. Do NOT change any other files
3. After making the change, run `dotnet build` from the repo root to verify 0 errors
4. Report the result — success or any errors
