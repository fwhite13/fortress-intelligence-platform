# Build Report: FAIT-DEVOPS-PAT

**Task:** Replace OAuth DevOps flow with PAT-based connection via dedicated table  
**Build Agent:** Tony Stark  
**Date:** 2026-03-12  
**Commit:** `ed6ae0cf1d0599459d0eb4ea8d6f9b351cf5ecf4`  
**Build Result:** ✅ 0 Error(s), 30 Warning(s) (all pre-existing)

---

## Files Deleted

| File | Reason |
|------|--------|
| `src/FortressAI.Shared/Models/UserDevOpsToken.cs` | OAuth-era model, replaced by dedicated connection model |
| `src/FortressAI.Web/Services/DevOpsTokenService.cs` | OAuth token exchange service, replaced by `DevOpsConnectionService` |

---

## Files Added

| File | Purpose |
|------|---------|
| `src/FortressAI.Shared/Models/UserDevOpsConnection.cs` | New model: `UserId` (PK), `OrgUrl`, `PatEncrypted`, `CreatedAt`, `UpdatedAt` |
| `src/FortressAI.Web/Services/DevOpsConnectionService.cs` | PAT save/read/disconnect/test; DataProtection encryption |

---

## Files Modified

| File | Changes |
|------|---------|
| `src/FortressAI.Web/Data/AppDbContext.cs` | Removed `UserDevOpsTokens` DbSet + entity config; added `UserDevOpsConnections` DbSet + entity config |
| `src/FortressAI.Web/Services/DatabaseInitializationService.cs` | Replaced `user_devops_tokens` extraTable entry with `user_devops_connections`; removed OAuth MCP server seed |
| `src/FortressAI.Web/Program.cs` | Replaced `DevOpsTokenService` registration with `DevOpsConnectionService`; removed `/auth/devops-callback` MapGet block |
| `src/FortressAI.Web/Components/Pages/Settings.razor` | Full DevOps card rewrite: PAT input fields + Test Connection + Save; removed OAuth query param handling |

---

## Storage Design

**Table:** `user_devops_connections`
```sql
CREATE TABLE IF NOT EXISTS user_devops_connections (
    user_id CHAR(36) NOT NULL,
    org_url VARCHAR(512) NOT NULL,
    pat_encrypted LONGTEXT NOT NULL,
    created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (user_id),
    CONSTRAINT fk_devops_conn_user FOREIGN KEY (user_id) REFERENCES users (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

**Encryption:** `IDataProtectionProvider.CreateProtector("DevOpsPat")` — same ASP.NET Core Data Protection stack used elsewhere in FAIT. PAT is encrypted before insert, decrypted on read. OrgUrl stored in plaintext (not secret).

**DB note:** The old `user_devops_tokens` table is left in the database. Its `CREATE TABLE IF NOT EXISTS` entry was removed from code; no DROP issued. Idempotent — causes zero harm.

---

## PAT Auth Convention

Azure DevOps PAT authentication:
```
Authorization: Basic {base64(":{PAT}")}
```
Empty username, PAT as password — per Microsoft's documented convention. Implemented in `DevOpsConnectionService.TestConnectionAsync` and will be used by any future MCP transport integration.

---

## Test Connection Implementation

**Endpoint:** `GET {orgUrl}/_apis/projects?api-version=7.1`  
**Success:** HTTP 200 — parses `count` from JSON response, shows "Connected — N projects found"  
**401/403:** "Invalid PAT or insufficient permissions"  
**404:** "Organization URL not found — check the URL"  
**Network error/timeout:** Appropriate human-readable message  

The test is a dry-run — it does NOT save credentials. User must click "Save Connection" separately.

---

## McpHttpTransport URL Resolution — Integration Gap (follow-up)

`McpToolService.cs` resolves tool call endpoints via `server.EndpointUrl` from the `mcp_servers` row. There is **no per-user URL override mechanism** in the current transport layer.

**Current state:** No `mcp_servers` row seeded for Azure DevOps — intentional. The OAuth-era seed (slug `azdo`, endpoint `https://mcp.azure.com/devops`, auth_type `oauth2`) was removed.

**Follow-up task required:** Once Microsoft confirms the hosted Azure DevOps MCP endpoint URL, a new seed is needed:
- Slug: `azure-devops`
- Auth type: `bearer`
- `RequiresUserAuth: true`
- `EndpointUrl`: Microsoft-hosted endpoint (TBD)
- Transport layer will need to resolve the PAT from `DevOpsConnectionService` (not `McpConnectionService`) and construct the Basic auth header before calling the endpoint

This gap is documented and tracked — nothing is broken today because no tool calls route to the DevOps server yet.

---

## ECS Environment Variables

OAuth env vars (`AzureDevOps__ClientId`, `AzureDevOps__ClientSecret`, `AzureDevOps__TenantId`, `AzureDevOps__RedirectUri`) were **never added** to the `fred-dev` ECS task definition. Confirmed: no ECS cleanup required. They were consumed only in the deleted `DevOpsTokenService.cs` and the removed Program.cs seed block.

---

## Self-Review Checklist

- [x] All OAuth DevOps code removed (model, service, callback endpoint, DI registration)
- [x] `UserDevOpsToken` entity config removed from `OnModelCreating`
- [x] `UserDevOpsTokens` DbSet removed from `AppDbContext`
- [x] `user_devops_connections` table added to `extraTables` (CREATE IF NOT EXISTS)
- [x] `user_devops_tokens` removed from `extraTables` (table left in DB, not dropped)
- [x] `UserDevOpsConnection` model created with correct column mappings
- [x] `DevOpsConnectionService` registered as scoped in Program.cs
- [x] Settings.razor: `@inject DevOpsTokenService` replaced with `@inject DevOpsConnectionService`
- [x] Settings.razor: OAuth card replaced with PAT input + Test + Save
- [x] Settings.razor: `DevOpsConnected`/`DevOpsError` query params and snackbar handlers removed
- [x] Settings.razor: `_cache` inject removed (was only used for OAuth state)
- [x] `TestConnectionAsync` uses correct Basic auth convention (`:{PAT}` base64)
- [x] PAT cleared from component memory after save (`_devOpsPat = string.Empty`)
- [x] Build: 0 errors
- [x] Committed and pushed: `ed6ae0cf`
