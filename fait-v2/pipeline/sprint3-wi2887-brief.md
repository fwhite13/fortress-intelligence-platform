# BUILD BRIEF — ADO#2887 — FORGE KB Integration Service
**Sprint 3, Lane 1 | FAIT v2 Epic #2835 | §7.2 fip-mcp FORGE KB**

## Context
You are Tony Stark (software-engineer). You are implementing FAIT v2 Sprint 3, WI #2887.
FAIT v2 repo: `~/projects/fip/fait-v2/` | branch: `main`
Spec: `memory/projects/fait-v2-spec-2026-04-27.md` (§7.0, §7.1, §7.2, §7.4)
FORGE KB MCP Server spec: `memory/projects/forge-kb-mcp-server-spec-2026-04-27.md`

## What Was Built (Sprint 2, now on main)
- `FortressAI.V2.Web` Blazor Server app
- EF Core DB context with Users, MainAssistants, Projects, MemoryTopics, UserSessions
- `IUserAgentRuntime` / `FargateUserAgentRuntime` (Fargate per-user session)
- `IMemoryFileService` / `MemoryFileService` (S3-backed memory .md files)
- `IUserProvisioningService` / `UserProvisioningService`
- Main chat UI (`ChatView.razor`, `MessageBubble.razor`), Dual-pane layout
- Memory management UI (`MemoryManagerView`, `TopicList`, `TopicEditor`)
- `AssistantLoadingState.razor` (cold start UX)
- `fortress.css` + `FipTheme.cs` CSS-variable-driven UI

## Objective
Build the FORGE KB integration service layer in FAIT v2. This gives the Blazor app (and ultimately the Fargate harness) the ability to call FORGE KB search/add via `fip-mcp` on behalf of authenticated users.

The `fip-mcp` service is **already deployed and live** at `https://fip-mcp.fortressam.ai/mcp` (or via ALB). You are NOT building fip-mcp — you are building the client layer in FAIT v2 that calls it.

## What to Build

### 1. DB Migration: `mcp_servers` and `mcp_user_tokens` tables

Add to `FaitV2DbContext` and generate EF migration:

```sql
-- mcp_servers: registry of available MCP connectors
CREATE TABLE mcp_servers (
  id            CHAR(36)     NOT NULL PRIMARY KEY,
  name          VARCHAR(100) NOT NULL,           -- e.g. "forge-kb", "ms365", "search"
  endpoint_url  VARCHAR(500) NOT NULL,
  auth_type     ENUM('oauth_entra','api_key','none') NOT NULL DEFAULT 'oauth_entra',
  default_read  TINYINT(1)   NOT NULL DEFAULT 1,
  default_write TINYINT(1)   NOT NULL DEFAULT 0,
  is_active     TINYINT(1)   NOT NULL DEFAULT 1,
  created_at    DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE INDEX ix_mcp_servers_name (name)
) ENGINE=InnoDB;

-- mcp_user_tokens: per-user OAuth tokens for MCP connectors
CREATE TABLE mcp_user_tokens (
  id           CHAR(36)     NOT NULL PRIMARY KEY,
  user_id      CHAR(36)     NOT NULL,
  server_name  VARCHAR(100) NOT NULL,           -- FK-by-name to mcp_servers.name
  access_token TEXT         NOT NULL,           -- encrypted at rest (DataProtection)
  refresh_token TEXT,
  token_expires_at DATETIME(6),
  created_at   DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at   DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  INDEX ix_mcp_user_tokens_user_id (user_id),
  UNIQUE INDEX ix_mcp_user_tokens_user_server (user_id, server_name),
  CONSTRAINT fk_mcp_user_tokens_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;
```

**GuidFormat rule:** ALL `MySqlConnectionStringBuilder` usages MUST set `GuidFormat = MySqlGuidFormat.None`. This is already done in Program.cs for the keyring — ensure any new DB context factory usages follow the same pattern. varchar(36) for all GUID columns.

### 2. EF Core Models

Create `McpServer.cs` and `McpUserToken.cs` in `Data/Models/`. Register in `FaitV2DbContext`. Follow existing conventions (column mappings, snake_case, `HasColumnType("datetime(6)")`).

### 3. `IForgeKbService` + `ForgeKbService`

Create `Services/IForgeKbService.cs` and `Services/ForgeKbService.cs`.

```csharp
public interface IForgeKbService
{
    /// <summary>List KBs accessible to the current user.</summary>
    Task<IReadOnlyList<KbInfo>> ListKbsAsync(string entraOid, CancellationToken ct = default);

    /// <summary>Search a KB for content relevant to the query.</summary>
    Task<IReadOnlyList<KbSearchResult>> SearchKbAsync(string kbId, string query, int topK = 5, CancellationToken ct = default);

    /// <summary>Add content to a KB. Returns job ID for polling.</summary>
    Task<string> AddToKbAsync(string kbId, string content, Dictionary<string, string> metadata, CancellationToken ct = default);

    /// <summary>Get metadata for a KB.</summary>
    Task<KbMetadata> GetKbMetadataAsync(string kbId, CancellationToken ct = default);
}

public record KbInfo(string KbId, string KbType, string Description, bool Writable);
public record KbSearchResult(string Content, object Metadata, double RelevanceScore);
public record KbMetadata(string KbId, string KbType, int DocumentCount, DateTime LastUpdated, string DataSourceId);
```

**Implementation:** `ForgeKbService` makes MCP JSON-RPC calls over HTTP to `fip-mcp`:
- `POST {FipMcpEndpointUrl}/mcp` with `Authorization: Bearer {userEntraToken}`
- Body: standard MCP JSON-RPC 2.0: `{"jsonrpc":"2.0","id":"<uuid>","method":"tools/call","params":{"name":"<tool>","arguments":{...}}}`
- Response: parse `result.content[0].text` (MCP standard tool result)

Use `IHttpClientFactory` for the HTTP client (already registered in Program.cs). Name the client `"FipMcpClient"`.

Config key: `FipMcp:EndpointUrl` (e.g. `https://api.fortressam.ai/mcp`)

**Auth:** The service needs the current user's Entra bearer token to pass to fip-mcp. Use `IHttpContextAccessor` to get the current user's auth token from the cookie/session. The FIP cookie contains a claims principal — extract the `access_token` claim or use a `ITokenProvider` abstraction.

Create `Services/IFipTokenProvider.cs` + `Services/FipTokenProvider.cs`:
```csharp
public interface IFipTokenProvider
{
    Task<string?> GetAccessTokenAsync();
}
```
For now, implement it by reading from `IHttpContextAccessor` claims. Future: exchange for a token via MSAL.

### 4. Seed `mcp_servers` table with FORGE KB entry

In `UserProvisioningService` or via EF Core data seeding (your choice — data seeding via `OnModelCreating` is fine for static known records), seed the `forge-kb` MCP server entry on startup:

```json
{
  "name": "forge-kb",
  "endpoint_url": "{FipMcp:EndpointUrl}",
  "auth_type": "oauth_entra",
  "default_read": true,
  "default_write": false,
  "is_active": true
}
```

Use `IDbContextFactory<FaitV2DbContext>` for any seeding done at startup to avoid scoped/singleton conflicts.

### 5. Register services in Program.cs

```csharp
builder.Services.AddHttpClient("FipMcpClient");
builder.Services.AddScoped<IFipTokenProvider, FipTokenProvider>();
builder.Services.AddScoped<IForgeKbService, ForgeKbService>();
```

Add config value: `FipMcp:EndpointUrl` in `appsettings.json` (default: `https://api.fortressam.ai/mcp`).

### 6. Wire `IForgeKbService` into Dashboard and Chat

- **Dashboard.razor**: call `IForgeKbService.ListKbsAsync()` to show accessible KBs (or count) in the sidebar/dashboard. Display as a simple list or count badge — UX is minimal, functional is the goal.
- **ChatView.razor**: make `IForgeKbService` available for injection. Don't wire active search yet (that's agent-side work) — just ensure it's injectable and available for future turns.

### 7. Acceptance Criteria (all must pass)
- [ ] EF migration runs cleanly: `mcp_servers` and `mcp_user_tokens` tables created
- [ ] `IForgeKbService.ListKbsAsync()` calls `fip-mcp` `list_kbs` tool and returns results
- [ ] `IForgeKbService.SearchKbAsync()` calls `fip-mcp` `search_kb` tool with correct JSON-RPC body
- [ ] `ForgeKbService` uses `IHttpClientFactory` (named "FipMcpClient"), not raw HttpClient
- [ ] `GuidFormat = MySqlGuidFormat.None` on ALL DB connection string builders
- [ ] `mcp_servers` seeded with `forge-kb` entry on startup (idempotent)
- [ ] Dashboard displays accessible KBs or KB count from live `list_kbs` call
- [ ] Service registered in DI, compiles cleanly, no build errors
- [ ] CC used via Claude Code CLI (mandatory)

## Mandatory Rules
- **CC CLI MANDATORY:** `cat brief.md | claude --model sonnet --print --dangerously-skip-permissions`
- CC env vars: `CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30`
- Work dir: `~/projects/fip/fait-v2/`
- Commit: `feat(fait-v2#2887): FORGE KB integration service`
- No hardcoded colors/sizes/fonts — CSS variables only
- No Cognito, Entra-only
- Dockerfile.debian only
- varchar(36) for all GUID columns
- GuidFormat=None on ALL MySQL connections

## ADO Work Item Updates (MANDATORY — post as Fred White via mcporter)
After each stage, post comment with attribution prefix:
- After BUILD: `**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED.`
- Use: `mcporter call devops.add_comment --args '{"project":"Fortress","id":2887,"text":"..."}'`

## Deliverables
1. Build Report at `~/projects/fip/fait-v2/pipeline/ADO2887-BUILD-REPORT.md`
2. All changes committed and pushed to `origin/main`
3. ADO WI #2887 comment added with commit hash
