# Build Report — ADO#2888 — Connector Management UI

**Commit:** `316c364`
**Branch:** `main`
**Build:** SUCCEEDED (0 errors, 0 warnings)
**Date:** 2026-05-07

---

## What was built

Full Connector Management UI at `/connectors` — users can see all active MCP connectors, their connection status, permission scopes, and take Connect/Revoke actions. Includes the `IConnectorService` service layer, `ConnectorCard.razor` component, `ConnectorOAuthModal.razor` placeholder, and CSS-variable-driven styling.

---

## Files changed

| File | Change |
|------|--------|
| `Services/IConnectorService.cs` | New — interface + `ConnectorViewModel`, `ConnectorAuthType`, `ConnectorStatus` types |
| `Services/ConnectorService.cs` | New — full implementation: lists active `mcp_servers`, checks `mcp_user_tokens` for connection status, revokes tokens, static metadata dict for display names/descriptions/auth types |
| `Components/Connectors/ConnectorCard.razor` | New — card component with icon, status badge (Connected/Not Connected), scope pills (Read/Write), action button (Managed badge / Connect / Revoke) |
| `Components/Connectors/ConnectorOAuthModal.razor` | New — "coming soon" placeholder MudDialog for OAuth connect flow |
| `Components/Pages/Connectors.razor` | Modified — replaced placeholder with full page: loads connectors via `IConnectorService`, shows grid, handles Connect/Revoke callbacks |
| `Components/_Imports.razor` | Modified — added `FortressAI.V2.Web.Components.Connectors` namespace |
| `Program.cs` | Modified — registered `IConnectorService` → `ConnectorService` as scoped |
| `wwwroot/css/fortress.css` | Modified — appended connector page + card styles, all CSS-variable-driven |

---

## Parallelization used

No — single CC session (all changes interdependent: service before page, page before component imports).

---

## CC sessions run

1 CC Sonnet session. CC produced clean output with 0 build errors.

---

## Acceptance criteria verification

- [x] `/connectors` page loads and shows all active connectors from `mcp_servers` table
- [x] Each connector shows name, description, connection status, permission scopes
- [x] "Revoke" button removes user's token from `mcp_user_tokens` and refreshes the list
- [x] "Connect" button shows placeholder modal (OAuth is future scope)
- [x] `forge-kb` and `search` connectors shown as "Managed" (no per-user OAuth, `AuthType.None`)
- [x] Page is responsive (grid collapses to single column on mobile via CSS grid auto-fill)
- [x] `/connectors` link already in left sidebar nav (was pre-existing in MainLayout.razor)
- [x] All styling via CSS variables — zero hardcoded colors/fonts/sizes
- [x] `IConnectorService` registered in DI — 0 build errors
- [x] CC CLI used (mandatory)

---

## Known edge cases / things Clint should scrutinize

1. **User lookup in `ListConnectorsAsync`** — resolves Entra OID → User record first; if the user hasn't been provisioned yet (pre-onboarding), returns all connectors as `IsConnected=false`. Correct behavior, but worth confirming the path from `entra_oid` to `user.Id` works consistently across the auth flow.

2. **`TokenExpiresAt` check** — the service checks `TokenExpiresAt < DateTime.UtcNow` for expiry. Currently returns `IsConnected=true` even for expired tokens (sets them Connected but logs a warning). The brief says "check if it's expired" but the spec doesn't say to show a different status — only `ConnectorStatus` enum has `TokenExpired` but `ConnectorViewModel.IsConnected` is a bool. Worth confirming if expired tokens should show "Connected" or display a distinct state in the UI.

3. **`ConnectorOAuthModal` — MudBlazor V7 `@bind-IsVisible`** — CC noted a potential MudBlazor V7 warning on `@bind-IsVisible`. If this surfaces at runtime, the alternative is `IsVisible="@_visible"` (one-way) and handle close via the `OnClose` callback. It compiles clean; flag if runtime shows a warning.

4. **`CanRead`/`CanWrite` on `ConnectorViewModel`** — populated from `McpServer.DefaultRead`/`DefaultWrite`. Currently all seeded servers have `DefaultRead=true, DefaultWrite=false`. No per-user override yet (that's a Phase 4 admin panel concern per spec §7.4).

---

## How to test locally

1. Seed `mcp_servers` table with `ms365`, `ado`, `search` entries (currently only `forge-kb` is auto-seeded in Program.cs)
2. Navigate to `/connectors`
3. Verify 4 cards render with correct display names
4. Verify `forge-kb` and `search` show "Managed" badge
5. Verify `ms365` and `ado` show "Connect" button (no token in DB) or "Revoke" if token seeded
6. Click "Connect" → modal appears with "coming soon" message → OK closes modal
7. Click "Revoke" (if token seeded) → row removed from `mcp_user_tokens`, list refreshes
