# ADO#2910 — Review Report

**Reviewer:** Hawkeye (code-review cycle 1)
**Date:** 2026-05-07
**Commit:** `157c7c6`
**Branch:** `main`
**Verdict:** PASS

---

## Checklist

| # | Check | Result |
|---|-------|--------|
| 1 | `appsettings.json` has `FipMcp:BaseUrl` pointing to `https://mcp.fortressam.ai/mcp` | PASS |
| 2 | `ForgeKbService` reads `FipMcp:BaseUrl`, calls `{baseUrl}/forge-kb` — no double `/mcp` | PASS |
| 3 | `Program.cs` seeds all 4 servers (forge-kb, ms365, ado, web-search) idempotently with correct URLs and auth types | PASS |
| 4 | `ConnectorService` uses `web-search` in both `ConnectorMeta` and `ManagedConnectors` | PASS |
| 5 | No remaining hardcoded `api.fortressam.ai` references in `src/` | PASS |

## Detailed Findings

### Critical
None.

### Important
None.

### Nitpick

1. **`EndpointUrl` in DB model vs config key rename** — The config key was renamed from `FipMcp:EndpointUrl` to `FipMcp:BaseUrl`, but the `McpServer.EndpointUrl` DB column retains its original name. This is correct — the DB column describes the per-server endpoint URL, while the config key is the base URL prefix. No action needed; noting for clarity.

2. **forge-kb auth type is `"none"`** — The seed sets `forge-kb` auth to `"none"`, matching the existing `ForgeKbService` which attaches a Bearer token from `IFipTokenProvider` at the HTTP level (not MCP auth). Consistent with current behavior.

## File-by-File Review

### `appsettings.json` (lines 52-54)
- Key renamed from `FipMcp:EndpointUrl` → `FipMcp:BaseUrl`. Domain corrected from `api.fortressam.ai` to `mcp.fortressam.ai`. Clean.

### `Services/ForgeKbService.cs` (lines 95-96, 115)
- Config read: `_config["FipMcp:BaseUrl"]` with `TrimEnd('/')` — correct.
- URL construction: `$"{baseUrl}/forge-kb"` — no double `/mcp`. Fixed as described in WI.

### `Program.cs` (lines 176-225)
- 4-server seed loop with idempotent upsert logic. Existing entries are updated if URL or auth type drifts. `SaveChangesAsync` called once after loop. Clean.
- Auth types: `forge-kb`=none, `ms365`=oauth_entra, `ado`=oauth_entra, `web-search`=none. Correct.

### `Services/ConnectorService.cs` (lines 17, 23)
- `ConnectorMeta` key: `"web-search"` with display name "Web Search". Correct.
- `ManagedConnectors` set: contains `"forge-kb"` and `"web-search"`. Correct.

## CC Invocation
```
Review performed by Hawkeye agent in Claude Code session (cycle 1).
Files read: appsettings.json, ForgeKbService.cs, Program.cs, ConnectorService.cs.
Grep scan: no api.fortressam.ai references remain; EndpointUrl references are DB model/migration only.
```
