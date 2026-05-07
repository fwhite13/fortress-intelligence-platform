# ADO#2910 — Build Report

**Agent:** Tony Stark (BUILD cycle 1)
**Date:** 2026-05-07
**Commit:** `157c7c6`
**Branch:** `main`
**Build:** SUCCEEDED (0 errors, 0 warnings)

---

## Changes

### 1. appsettings.json
- Renamed `FipMcp:EndpointUrl` to `FipMcp:BaseUrl`
- Fixed URL from `https://api.fortressam.ai/mcp` to `https://mcp.fortressam.ai/mcp`

### 2. Services/ForgeKbService.cs
- Updated config key reference from `FipMcp:EndpointUrl` to `FipMcp:BaseUrl`
- Fixed URL construction: was `{endpointUrl}/mcp` (producing double `/mcp`), now `{baseUrl}/forge-kb`

### 3. Program.cs — MCP server seeding
- Replaced single `forge-kb` seed block with idempotent 4-server loop
- Now seeds: `forge-kb`, `ms365`, `ado`, `web-search`
- Each server gets URL `{BaseUrl}/{name}` with correct auth type
- Existing entries are updated if URL or auth type changed

### 4. Services/ConnectorService.cs
- Renamed `ConnectorMeta` key from `"search"` to `"web-search"`
- Updated `ManagedConnectors` set from `"search"` to `"web-search"`

## Files Changed
| File | Change |
|------|--------|
| `src/FortressAI.V2.Web/appsettings.json` | Config key rename + URL fix |
| `src/FortressAI.V2.Web/Program.cs` | 4-server seed loop |
| `src/FortressAI.V2.Web/Services/ForgeKbService.cs` | URL construction fix |
| `src/FortressAI.V2.Web/Services/ConnectorService.cs` | search → web-search rename |

## Verification
- `dotnet build`: 0 errors, 0 warnings
- No EF migration needed (schema unchanged, rows seeded at startup)
- DNS note: `mcp.fortressam.ai` CNAME pending Rob Nethery — code is correct regardless
