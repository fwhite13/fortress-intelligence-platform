# CC Brief: ADO4559 Review Cycle 2 Fixes

## Task
Two targeted fixes to the WebFetch implementation. No other changes.

## Fix 1 — Program.cs
File: `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs`

Add a named `"WebFetch"` HttpClient registration with a configured primary handler. 
Find the existing named HttpClient registrations (the block with "devops-test", "azure-devops", "mcp-transport", etc.) and add the following BEFORE the "devops-test" registration:

```csharp
// Named HttpClient for WebFetch — enforces 3-redirect limit per spec
builder.Services.AddHttpClient("WebFetch")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3
    });
```

The exact insertion point — add it BEFORE this line:
```
// Named HttpClient for DevOps test connection — short timeout so bad org URL fails fast
builder.Services.AddHttpClient("devops-test", client =>
```

## Fix 2 — WebFetchClient.cs
File: `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/WebFetchClient.cs`

Remove the dead `handler` variable in `FetchAsync`. It was instantiated but never passed to the factory — dead code.

Delete these lines from the `FetchAsync` method:
```csharp
            var handler = new HttpClientHandler
            {
                MaxAutomaticRedirections = 3,
                AllowAutoRedirect = true,
            };

```

The line immediately after the deleted block should be:
```csharp
            var client = _httpClientFactory.CreateClient("WebFetch");
```

## Constraints
- Do NOT touch any other code
- Do NOT modify any other files
- These are the only two changes

## Acceptance Criteria
1. Program.cs has a `builder.Services.AddHttpClient("WebFetch").ConfigurePrimaryHttpMessageHandler(...)` registration
2. WebFetchClient.cs no longer has the dead `handler` variable
3. `_httpClientFactory.CreateClient("WebFetch")` remains in WebFetchClient.cs (already correct — don't change it)
