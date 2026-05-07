# BUILD BRIEF — ADO#2888 — Connector Management UI
**Sprint 3, Lane 2 | FAIT v2 Epic #2835 | §7.3, §7.4**
**Agent:** Tony Stark | **Cycle:** 1 | **Date:** 2026-05-07

---

## Context

You are Tony Stark (software-engineer). You are implementing FAIT v2 Sprint 3, WI #2888.
FAIT v2 repo: `~/projects/fip/fait-v2/` | branch: `main`
Spec: `memory/projects/fait-v2-spec-2026-04-27.md` (§5.2 Connector Management, §7.3, §7.4)
Current HEAD: `f8a8c00`

---

## What's Already Built (on main)

- `mcp_servers` and `mcp_user_tokens` DB tables + EF models (built by WI#2887)
- `IForgeKbService` + `ForgeKbService` — calls fip-mcp via HTTP (built by WI#2887)
- `FipTokenProvider` + `FipPortalDbContext` — reads user tokens from `fip_dev.user_microsoft_tokens` (built by WI#2887)
- `Components/Pages/Connectors.razor` — currently a placeholder page, needs implementation
- `fortress.css` + `FipTheme.cs` — CSS-variable-driven UI system
- Full Blazor app shell: nav sidebar, layout, auth

---

## Objective

Build the Connector Management UI at `/connectors`. This is the page where users manage their MCP connector connections — see which connectors are available, connect/authorize, revoke, and view permission scopes.

---

## What to Build

### 1. `IConnectorService` + `ConnectorService`

Create `Services/IConnectorService.cs` and `Services/ConnectorService.cs`.

```csharp
public interface IConnectorService
{
    /// <summary>List all active MCP connectors accessible to this user.</summary>
    Task<IReadOnlyList<ConnectorViewModel>> ListConnectorsAsync(string entraOid, CancellationToken ct = default);

    /// <summary>Get the user's connection status for a specific connector.</summary>
    Task<ConnectorStatus> GetConnectionStatusAsync(string entraOid, string serverName, CancellationToken ct = default);

    /// <summary>Revoke a user's OAuth token for a connector.</summary>
    Task RevokeConnectionAsync(string entraOid, string serverName, CancellationToken ct = default);
}

public record ConnectorViewModel(
    string Name,            // e.g. "forge-kb", "ms365"
    string DisplayName,     // e.g. "FORGE Knowledge Base", "Microsoft 365"
    string Description,
    bool IsConnected,
    bool CanRead,
    bool CanWrite,
    ConnectorAuthType AuthType,
    DateTime? ConnectedAt
);

public enum ConnectorAuthType { OAuthEntra, ApiKey, None }
public enum ConnectorStatus { Connected, NotConnected, TokenExpired }
```

**Implementation:**
- Read from `mcp_servers` table (via `IDbContextFactory<FaitV2DbContext>`) for the server list
- Check `mcp_user_tokens` for user's connection status (has a token, is it expired?)
- `RevokeConnectionAsync` deletes the user's row from `mcp_user_tokens`
- Use `IFipTokenProvider` to get user's Entra OID

**Display names mapping** (hardcode in service, or use a static dictionary):
```csharp
private static readonly Dictionary<string, (string DisplayName, string Description)> ConnectorMeta = new()
{
    ["forge-kb"]  = ("FORGE Knowledge Base", "Search and add to your organization's knowledge bases"),
    ["ms365"]     = ("Microsoft 365", "Email, calendar, Teams, OneDrive, SharePoint"),
    ["search"]    = ("Web Search", "Search the web via Brave Search API"),
    ["ado"]       = ("Azure DevOps", "Work items, repos, pipelines"),
    ["ms365"]     = ("Microsoft 365", "Email, calendar, Teams, SharePoint"),
};
```

### 2. `Components/Pages/Connectors.razor` — replace placeholder

Replace the existing placeholder with a full implementation:

```razor
@page "/connectors"
@attribute [Authorize]

<PageTitle>Connectors — FAIT</PageTitle>

<div class="connectors-page">
    <div class="connectors-header">
        <h1>Connectors</h1>
        <p class="connectors-subtitle">Connect your tools to give your AI assistant access to the right data.</p>
    </div>
    
    @if (_loading)
    {
        <MudProgressCircular Indeterminate="true" />
    }
    else
    {
        <div class="connectors-grid">
            @foreach (var connector in _connectors)
            {
                <ConnectorCard Connector="connector" OnRevoke="HandleRevoke" OnConnect="HandleConnect" />
            }
        </div>
    }
</div>
```

Key behaviors:
- On load: call `IConnectorService.ListConnectorsAsync()` with current user's Entra OID
- Show loading state while fetching
- Display connector cards in a responsive grid
- "Connect" button → triggers OAuth flow (for now: show a "Coming soon" or placeholder modal — actual OAuth is WI#2889/2890 scope)
- "Revoke" button → calls `RevokeConnectionAsync`, refreshes list
- For `forge-kb` and `search` connectors: show "Active" (service-level, no per-user OAuth needed)

### 3. `Components/Connectors/ConnectorCard.razor`

Card component for a single connector:

```razor
@* Shows: connector icon/name, description, connection status badge, read/write scope pills, Connect/Revoke button *@
```

Layout:
- Connector name + icon (use MudIcon or emoji placeholder if no icon)
- Description text
- Status badge: `Connected` (green), `Not Connected` (gray), `Token Expired` (orange)
- Permission pills: `Read` / `Write` using CSS variables
- Action button:
  - If `IsConnected` + `AuthType == OAuthEntra` → "Revoke" button (MudButton, outlined, error color)
  - If `!IsConnected` + `AuthType == OAuthEntra` → "Connect" button (MudButton, filled, primary color)
  - If `AuthType == None` or `AuthType == ApiKey` → "Managed" badge (no button)
- All colors via CSS variables — NO hardcoded hex or named colors

### 4. `Components/Connectors/ConnectorOAuthModal.razor` (placeholder)

Placeholder modal that shows when user clicks "Connect":
```razor
@* Shows: "OAuth authorization for [ConnectorName] is coming soon. Your admin will enable this connector." *@
@* MudDialog with OK button *@
```

This satisfies the UX without requiring the actual OAuth redirect flow (which is WI#2889/2890 scope).

### 5. Register `IConnectorService` in Program.cs

```csharp
builder.Services.AddScoped<IConnectorService, ConnectorService>();
```

### 6. Add `/connectors` to the nav sidebar

In the left nav component (wherever nav items are defined), add a "Connectors" nav item:
- Icon: `Icons.Material.Outlined.Cable` or `SettingsEthernet`
- Label: "Connectors"
- Route: `/connectors`

### 7. Acceptance Criteria
- [ ] `/connectors` page loads and shows all active connectors from `mcp_servers` table
- [ ] Each connector shows name, description, connection status, permission scopes
- [ ] "Revoke" button removes user's token from `mcp_user_tokens` and refreshes the list
- [ ] "Connect" button shows placeholder modal (OAuth is future scope)
- [ ] `forge-kb` and `search` connectors shown as "Managed" (no per-user OAuth)
- [ ] Page is responsive (grid collapses to single column on mobile)
- [ ] `/connectors` link in left sidebar nav
- [ ] All styling via CSS variables — zero hardcoded colors/fonts/sizes
- [ ] `IConnectorService` registered in DI, 0 build errors
- [ ] CC CLI used (mandatory)

---

## Mandatory Rules
- **CC CLI MANDATORY:**
  ```bash
  CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
  cat brief.md | claude --model sonnet --print --dangerously-skip-permissions
  ```
- Work dir: `~/projects/fip/fait-v2/`
- Commit: `feat(fait-v2#2888): Connector Management UI`
- No hardcoded colors/sizes/fonts — ALL via CSS variables from `fortress.css`
- No `@{ var x = ... }` inside Razor `@if/@else` blocks with markup — use `@code` properties
- MudBlazor: use base icon names only (no `Rounded`, `Sharp`, `TwoTone` variants)
- varchar(36) for GUID columns, GuidFormat=None on ALL MySQL connections
- No Cognito, Entra-only auth

---

## ADO Comment (MANDATORY)
```bash
mcporter call devops.add_comment --args '{"project":"Fortress","id":2888,"text":"**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED."}'
```

---

## Deliverables
1. Build Report at `~/projects/fip/fait-v2/pipeline/ADO2888-BUILD-REPORT.md`
2. All changes committed and pushed to `origin/main`
3. ADO WI #2888 comment with commit hash
4. Report back to Maria
