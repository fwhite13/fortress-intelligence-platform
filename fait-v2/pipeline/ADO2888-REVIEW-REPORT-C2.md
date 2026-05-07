# Review Report — ADO#2888 (Connector Management UI) — Cycle 2

### Verdict: ✅ PASS

**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-05-07
**Commit:** `1984873` — `fix(fait-v2#2888): forge-kb auth_type seed, null OID handling, IDialogService modal, DB auth_type mapping`
**Branch:** `main`

---

## CC Review Summary

All 5 cycle 1 issues verified fixed via Claude Code review of the 4 changed files:
- `Program.cs`
- `Components/Pages/Connectors.razor`
- `Components/Connectors/ConnectorOAuthModal.razor`
- `Services/ConnectorService.cs`

No false positives dismissed. No regressions introduced. One pre-existing placeholder noted for follow-up.

---

## Spec Compliance Check

All cycle 1 issues were targeted fixes. Scope confirmed correct — only the 4 files in the commit diff were touched.

---

## Consistency Audit

| Check | Result |
|---|---|
| `ConnectorOAuthModal` parameter name `ConnectorName` matches Connectors.razor `["ConnectorName"]` | ✅ Match |
| `MapAuthType()` string values match DB enum values (`"none"`, `"api_key"`, `"oauth_entra"`) | ✅ Match |
| `forge-kb` seed `AuthType = "none"` matches `MapAuthType` `"none"` → `ConnectorAuthType.None` | ✅ Match |
| `IDialogService.ShowAsync<ConnectorOAuthModal>` vs modal's `[CascadingParameter] MudDialogInstance` | ✅ Correct pattern |

---

## Issues Found

### Critical Issues: 0

### Important Issues: 0

### Nitpicks: 1

**N1:** `connectedAt` always `null` in `ListConnectorsAsync` — `ConnectorService.cs` line ~77/96. The field is set to `null` unconditionally; no token/connection timestamp is ever read from DB. Pre-existing placeholder, not introduced by this fix. The UI likely hides or ignores the value for now. Track as a follow-up WI.

---

## Cycle 1 Issue Verification

### C1 — forge-kb seed auth_type FIXED ✅

Both INSERT path and UPDATE (correction) path correctly set `AuthType = "none"`.  
The UPDATE branch runs an idempotent correction: `if (server.AuthType != "none") { server.AuthType = "none"; }` — any existing row with wrong value is patched on next startup.  
Only one seed entry (`forge-kb`) exists; no other entries at risk.

### C2 — LoadConnectors() null _entraOid guard FIXED ✅

```csharp
if (string.IsNullOrEmpty(_entraOid))
{
    _error = "Unable to identify your account. Please sign out and sign in again.";
    _loading = false;
    return;
}
```

Sets `_error`, clears loading state, returns early. No silent empty page.

### C3 — HandleRevoke() null _entraOid guard FIXED ✅

```csharp
if (string.IsNullOrEmpty(_entraOid))
{
    Snackbar.Add("Unable to identify your account. Please sign out and sign in again.", Severity.Error);
    return;
}
```

Snackbar error + early return. Clear user feedback.

### I1 — ConnectorOAuthModal proper MudDialog FIXED ✅

**Modal:**
```razor
[CascadingParameter] MudDialogInstance MudDialog { get; set; } = null!;
private void Close() => MudDialog.Close();
```

No inline `@if` toggle. Proper cascading parameter. Proper close call.

**Connectors.razor:**
```razor
@inject IDialogService DialogService

var parameters = new DialogParameters { ["ConnectorName"] = connector.DisplayName };
await DialogService.ShowAsync<ConnectorOAuthModal>(
    $"Connect {connector.DisplayName}",
    parameters,
    new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true }
);
```

`IDialogService.ShowAsync<ConnectorOAuthModal>` — correct pattern.

### I2 — ConnectorService reads AuthType from DB FIXED ✅

```csharp
private static ConnectorAuthType MapAuthType(string? dbAuthType) => dbAuthType switch
{
    "oauth_entra" => ConnectorAuthType.OAuthEntra,
    "api_key"     => ConnectorAuthType.ApiKey,
    "none"        => ConnectorAuthType.None,
    _             => ConnectorAuthType.OAuthEntra  // safe default for unknown
};

// In ListConnectorsAsync:
var authType = MapAuthType(server.AuthType);  // reads DB column, not hardcoded
```

`ConnectorMeta` AuthType is explicitly discarded (`_`). DB value is authoritative.

---

## Regression Scan

| Check | Result |
|---|---|
| New hardcoded values that should come from DB | None. `ConnectorMeta` provides display metadata only; its AuthType is discarded. |
| New unguarded `_entraOid` references | `HandleConnect` doesn't use `_entraOid` — opens dialog only. No regression. |
| Remaining inline modal patterns | None. No `@if` toggle remnants. |
| Logic errors in the fix | None found. |

---

## Positive Observations

- The UPDATE correction branch in `Program.cs` is a nice self-healing pattern — corrects any existing wrong seed rows on startup without manual migration.
- `ConnectorMeta` AuthType discard (`_`) is a clean explicit signal that the DB column is authoritative.
- Snackbar error in `HandleRevoke` and `_error` state in `LoadConnectors` use different but appropriate UX patterns for their contexts (transient action vs. page-level failure).

---

## Verdict: PASS

All 5 cycle 1 issues confirmed fixed. No regressions. One pre-existing placeholder (N1) tracked for follow-up but not blocking.

Code ships.
