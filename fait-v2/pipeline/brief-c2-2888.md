# BUILD BRIEF — ADO#2888 — Connector Management UI (Cycle 2 — Review Fixes)
**Sprint 3, Lane 2 | FAIT v2 Epic #2835**
**Agent:** Tony Stark | **Cycle:** 2 | **Date:** 2026-05-07

---

## Context

Cycle 2 — fix 5 issues from Clint's C1 review. Only touch what's listed.

**Repo:** `~/projects/fip/fait-v2/` | **Branch:** `main` | **Current HEAD:** `316c364`

---

## Fix C1 — forge-kb seeded with wrong `auth_type`

**Problem:** `Program.cs` seeds forge-kb with `auth_type = "oauth_entra"` but `ConnectorService.ConnectorMeta` maps it to `ConnectorAuthType.None` (Managed). DB value is wrong.

**Fix in `Program.cs` seeding:**
- forge-kb seed: `auth_type = "none"` (not `"oauth_entra"`)
- search seed: `auth_type = "none"` (if similarly wrong)
- Add `AuthType = "none"` to the update/upsert branch so existing rows get corrected on next startup

---

## Fix C2 — Null `entraOid` silent empty page

**Problem:** `LoadConnectors()` silently returns when OID is null — user sees a blank grid.

**Fix in `Connectors.razor`:**
```csharp
var oid = _httpContextAccessor.HttpContext?.User?.FindFirst("oid")?.Value
       ?? _httpContextAccessor.HttpContext?.User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

if (string.IsNullOrEmpty(oid))
{
    _error = "Unable to identify your account. Please sign out and sign in again.";
    _loading = false;
    return;
}
```

---

## Fix C3 — Null `entraOid` in HandleRevoke is silent no-op

**Problem:** `HandleRevoke` early-returns with no user feedback when OID is null.

**Fix in `Connectors.razor`:**
```csharp
if (string.IsNullOrEmpty(oid))
{
    Snackbar.Add("Unable to identify your account. Please sign out and sign in again.", Severity.Error);
    return;
}
```

---

## Fix I1 — ConnectorOAuthModal renders inline without IDialogService

**Problem:** Modal is rendered inline with conditional show/hide — no proper MudDialog behavior (no backdrop, no ESC, no click-outside).

**Fix:** Use `IDialogService` to open the modal as a proper MudDialog:

In `Connectors.razor`:
```csharp
[Inject] IDialogService DialogService { get; set; } = null!;

private async Task HandleConnect(ConnectorViewModel connector)
{
    var parameters = new DialogParameters
    {
        ["ConnectorName"] = connector.DisplayName
    };
    await DialogService.ShowAsync<ConnectorOAuthModal>(
        $"Connect {connector.DisplayName}",
        parameters,
        new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true }
    );
}
```

`ConnectorOAuthModal.razor` should be a proper `MudDialog` component with `[CascadingParameter] IMudDialogInstance MudDialog { get; set; }` and an OK button that calls `MudDialog.Close()`.

Remove any inline conditional rendering of the modal from `Connectors.razor`.

---

## Fix I2 — McpServer.AuthType DB column never read

**Problem:** `ConnectorService.ListConnectorsAsync` falls back to hardcoded `OAuthEntra` for unknown connectors instead of reading `mcp_servers.auth_type` from the DB.

**Fix in `ConnectorService.cs`:**
When building `ConnectorViewModel`, read the `auth_type` column from the `McpServer` entity and map it to `ConnectorAuthType`:

```csharp
private static ConnectorAuthType MapAuthType(string? dbAuthType) => dbAuthType switch
{
    "oauth_entra" => ConnectorAuthType.OAuthEntra,
    "api_key"     => ConnectorAuthType.ApiKey,
    "none"        => ConnectorAuthType.None,
    _             => ConnectorAuthType.OAuthEntra  // safe default for unknown
};
```

Use this when constructing `ConnectorViewModel` instead of the hardcoded fallback.

---

## Mandatory Rules

- **CC CLI MANDATORY:**
  ```bash
  CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
  cat brief-c2-2888.md | claude --model sonnet --print --dangerously-skip-permissions
  ```
- Work dir: `~/projects/fip/fait-v2/`
- Only fix the 5 listed issues — no scope creep
- Commit: `fix(fait-v2#2888): forge-kb auth_type seed, null OID handling, IDialogService modal, DB auth_type mapping`
- Run `dotnet build` — 0 errors, 0 warnings

---

## ADO Comment (MANDATORY)
```bash
mcporter call devops.add_comment --args '{"project":"Fortress","id":2888,"text":"**[Tony Stark — BUILD cycle 2]**\nCommit {hash}: C1-C3 critical + I1-I2 important fixes. Build: SUCCEEDED."}'
```

## Deliverables
1. Cycle 2 section appended to `~/projects/fip/fait-v2/pipeline/ADO2888-BUILD-REPORT.md`
2. Commit pushed to `origin/main`
3. ADO comment on #2888
4. Report back to Maria
