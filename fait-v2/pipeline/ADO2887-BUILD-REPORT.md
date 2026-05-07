# Build Report — ADO#2887 — FORGE KB Integration Service

**Sprint 3 | FAIT v2 | Tony Stark — BUILD cycle 1**
**Date:** 2026-05-07
**Commit:** `36aeeb1`
**Branch:** `main`
**Build:** SUCCEEDED (0 errors, 0 warnings)

---

## What Was Built

### 1. EF Core Models
- `Data/Models/McpServer.cs` — registry of MCP connectors (`mcp_servers` table)
- `Data/Models/McpUserToken.cs` — per-user OAuth tokens for MCP connectors (`mcp_user_tokens` table)

### 2. EF Migration — `AddMcpTables`
- File: `Data/Migrations/20260507125357_AddMcpTables.cs`
- Creates `mcp_servers`: id (varchar 36), name (unique), endpoint_url, auth_type (default oauth_entra), default_read, default_write, is_active, created_at
- Creates `mcp_user_tokens`: id, user_id (FK → users CASCADE), server_name, access_token (TEXT), refresh_token (TEXT), token_expires_at, created_at, updated_at
- Indexes: `ix_mcp_servers_name` (unique), `ix_mcp_user_tokens_user_id`, `ix_mcp_user_tokens_user_server` (unique composite)

### 3. IFipTokenProvider / FipTokenProvider
- `Services/IFipTokenProvider.cs` — interface: `GetAccessTokenAsync()`
- `Services/FipTokenProvider.cs` — reads `access_token` claim from `IHttpContextAccessor`

### 4. IForgeKbService / ForgeKbService
- `Services/IForgeKbService.cs` — interface + DTOs: `KbInfo`, `KbSearchResult`, `KbMetadata`
- `Services/ForgeKbService.cs` — MCP JSON-RPC 2.0 client over HTTP to `{FipMcp:EndpointUrl}/mcp`
  - Uses named `FipMcpClient` from `IHttpClientFactory`
  - Attaches `Authorization: Bearer {token}` from `IFipTokenProvider`
  - Implements: `ListKbsAsync` → `list_kbs`, `SearchKbAsync` → `search_kb`, `AddToKbAsync` → `add_to_kb`, `GetKbMetadataAsync` → `get_kb_metadata`
  - Extracts `result.content[0].text` from MCP JSON-RPC response

### 5. DI Registration (Program.cs)
```csharp
builder.Services.AddHttpClient("FipMcpClient");
builder.Services.AddScoped<IFipTokenProvider, FipTokenProvider>();
builder.Services.AddScoped<IForgeKbService, ForgeKbService>();
```

### 6. mcp_servers Seeding (Program.cs startup)
- Idempotent: checks `forge-kb` existence before insert
- Also updates `endpoint_url` if config changed
- Uses `IDbContextFactory<FaitV2DbContext>` (avoids scoped/singleton conflict)

### 7. Config — appsettings.json
```json
"FipMcp": {
  "EndpointUrl": "https://api.fortressam.ai/mcp"
}
```

### 8. Dashboard.razor
- Injects `IForgeKbService` and `AuthenticationState`
- After agent ready: calls `ListKbsAsync(entraOid)` and renders KB status bar with KB descriptions
- Non-critical: errors swallowed, empty list renders nothing

### 9. ChatView.razor
- `IForgeKbService` injected as `ForgeKbService` — available for future agent-side turns

---

## Acceptance Criteria Status

| Criterion | Status |
|-----------|--------|
| EF migration runs cleanly: mcp_servers and mcp_user_tokens created | PASS |
| `ListKbsAsync` calls `list_kbs` tool | PASS |
| `SearchKbAsync` calls `search_kb` with correct JSON-RPC body | PASS |
| `ForgeKbService` uses `IHttpClientFactory` (named "FipMcpClient") | PASS |
| `GuidFormat = MySqlGuidFormat.None` on all DB connections | PASS (existing pattern in Program.cs) |
| `mcp_servers` seeded with forge-kb entry on startup (idempotent) | PASS |
| Dashboard displays accessible KBs from live list_kbs call | PASS |
| Service registered in DI, compiles cleanly, no build errors | PASS |

---

## Build Output
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
