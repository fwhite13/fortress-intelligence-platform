# Review Report — ADO#2888: Connector Management UI

**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1  
**Commit:** `316c364`  
**Branch:** `main`  
**Date:** 2026-05-07  

### Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

No developer brief with formal §2/§7 sections was provided for this WI. Reviewed against the task description in the ADO ticket.

**Files modified — all expected:**
- `Services/IConnectorService.cs` — ✅ created
- `Services/ConnectorService.cs` — ✅ created
- `Components/Connectors/ConnectorCard.razor` — ✅ created
- `Components/Connectors/ConnectorOAuthModal.razor` — ✅ created
- `Components/Pages/Connectors.razor` — ✅ replaced placeholder
- `wwwroot/css/fortress.css` — ✅ connector CSS added
- `Program.cs` — ✅ DI registration + seed
- `Components/_Imports.razor` — ✅ namespace added

**Out of scope:** No extraneous changes detected.

**Spec compliance verdict:** ✅ COMPLIANT (scope), but NEEDS-CHANGES for correctness bugs listed below.

---

## CC Review Summary

Ran full adversarial Claude Code review against 10 targeted checks covering DB consistency, auth, null safety, UI patterns, and CSS. CC found 2 hard failures, 5 warnings, and 1 informational item. My verdict confirms all findings as real. No false positives dismissed.

---

## Consistency Audit

**Files Cross-Referenced:**
- `ConnectorService.ConnectorMeta` ("forge-kb" → `ConnectorAuthType.None`) ↔ `Program.cs` seed (`AuthType = "oauth_entra"`) — ❌ **MISMATCH**
- `ConnectorCard.razor` (Managed = `AuthType.None || AuthType.ApiKey`) ↔ `ConnectorService.ManagedConnectors` (`isConnected = true` for "forge-kb", "search") — ⚠️ Two independent gates, consistent today but fragile
- `McpServer.AuthType` DB column ↔ `ConnectorService` — DB value is **never read** (service uses hardcoded `ConnectorMeta`)
- `mcp_user_tokens.user_id` ↔ `RevokeConnectionAsync` — ✅ correctly scoped to requesting user via `entraOid → user.Id` lookup

---

## Critical Issues — 3

### C1: forge-kb seeded with wrong `auth_type`

- **File:** `src/FortressAI.V2.Web/Program.cs` (seed block, `AuthType = "oauth_entra"`)
- **Category:** Consistency / data correctness
- **Issue:** `Program.cs` seeds `forge-kb` with `AuthType = "oauth_entra"` but `ConnectorService.ConnectorMeta` maps it to `ConnectorAuthType.None` and `ManagedConnectors` treats it as always-connected. The DB column contains a factually incorrect value. `ConnectorService` doesn't read the column today, so runtime behavior is currently unaffected — but any future code (admin tooling, reporting, a second service) that reads `McpServer.AuthType` from the DB will get wrong data. The seed update path (the `else` branch that corrects `EndpointUrl`) never touches `AuthType`, so existing bad rows will persist.
- **Impact:** Wrong persistent data; silent time bomb for any consumer of `mcp_servers.auth_type`.
- **Fix:**
  ```diff
  - AuthType = "oauth_entra",
  + AuthType = "none",
  ```
  Also add `AuthType` correction to the seed update branch:
  ```csharp
  // in the existing else { } update block:
  if (server.AuthType != "none")
  {
      server.AuthType = "none";
      await seedDb.SaveChangesAsync();
  }
  ```

---

### C2: null `entraOid` — silent empty page

- **File:** `Components/Pages/Connectors.razor`, `LoadConnectors()` method
- **Category:** Correctness / user experience
- **Issue:** When `_entraOid` is null (claim not present, auth misconfiguration), `LoadConnectors()` silently skips the service call, sets `_loading = false`, leaves `_error = null`, and `_connectors` stays empty. The page renders an empty grid with no message. The user has no indication of why there are no connectors.
- **Impact:** Silent failure — user confusion, no actionable feedback.
- **Fix:**
  ```diff
  - if (_entraOid != null)
  -     _connectors = await ConnectorService.ListConnectorsAsync(_entraOid);
  + if (_entraOid == null)
  + {
  +     _error = "Authentication error: user identity could not be resolved. Please refresh the page.";
  +     return;
  + }
  + _connectors = await ConnectorService.ListConnectorsAsync(_entraOid);
  ```

---

### C3: null `entraOid` in `HandleRevoke` — silent no-op

- **File:** `Components/Pages/Connectors.razor`, `HandleRevoke()` method
- **Category:** Correctness
- **Issue:** `HandleRevoke()` guards `if (_entraOid == null) return;` with no user feedback. If Revoke is somehow triggered in this state (auth race, edge case), the user clicks Revoke and nothing happens — no snackbar, no error, the connector still shows as Connected.
- **Impact:** Silent failure on user action; confusing UX.
- **Fix:**
  ```diff
  - if (_entraOid == null) return;
  + if (_entraOid == null)
  + {
  +     Snackbar.Add("Authentication error. Please refresh the page.", Severity.Error);
  +     return;
  + }
  ```

---

## Important Issues — 2

### I1: `ConnectorOAuthModal` renders inline — no backdrop, no ESC, no click-outside

- **File:** `Components/Connectors/ConnectorOAuthModal.razor`, `Components/Pages/Connectors.razor`
- **Category:** Quality / UX
- **Issue:** `ConnectorOAuthModal` renders `<MudDialog>` inline inside a `@if (_showModal)` block rather than using `IDialogService`. Without `MudDialogProvider` involvement: (1) no dimmed backdrop/scrim renders, (2) ESC key does not dismiss, (3) clicking outside the dialog does not dismiss, (4) `DialogOptions` properties like `MaxWidth` are not processed. The `_dialogOptions` field in the component is effectively dead. Only the OK button closes the dialog.
- **Impact:** Substandard UX — modal doesn't behave like a modal.
- **Fix:** Inject `IDialogService` into `Connectors.razor` and use `ShowAsync<ConnectorOAuthModal>()` to open the dialog. Pass `ConnectorName` as a dialog parameter.

### I2: `McpServer.AuthType` DB column never read — undocumented dead field

- **File:** `Services/ConnectorService.cs` (ListConnectorsAsync, lines 63–65)
- **Category:** Quality / future maintainability
- **Issue:** `McpServer.AuthType` is fetched from the DB on every call but never inspected. The fallback for unknown connectors is hardcoded to `ConnectorAuthType.OAuthEntra`, ignoring the DB value entirely. If an operator adds a new server row with `auth_type="none"`, it will incorrectly show Connect/Revoke buttons. No warning is logged.
- **Impact:** Silent misclassification for any connector not in `ConnectorMeta`.
- **Fix (option A — simple):** Add a `ParseAuthType` helper and use the DB value in the fallback:
  ```csharp
  var authType = ConnectorMeta.TryGetValue(server.Name, out var m)
      ? m.AuthType
      : ParseAuthType(server.AuthType); // "oauth_entra" → OAuthEntra, "none" → None, etc.
  ```
  **Fix (option B — minimal):** Add startup/call-time warning log when a server is not in `ConnectorMeta`.

---

## Nitpicks — 4

- **N1:** `rgba(0,0,0,0.08)` in `.connector-card:hover` box-shadow (`fortress.css`). The rest of the file uses `color-mix(in srgb, var(--color-*) ...)` for transparency. Not a hard FIP token rule violation, but inconsistent with the established pattern.
- **N2:** Several hardcoded `rem` values in connector CSS (`font-size: 1.75rem`, `font-size: 0.75rem`, `font-size: 0.875rem`, `font-size: 0.7rem`) and spacing (`gap: 0.25rem`, `gap: 0.5rem`) not using `var(--spacing-*)`. Consistent with some pre-existing patterns in the file but diverges from the token-first approach.
- **N3:** `GetConnectionStatusAsync` and `ConnectorStatus` enum are defined and implemented but never called from any component. `ConnectorViewModel.IsConnected` collapses `Connected` and `TokenExpired` into one bool, making the richer status unreachable. Dead code today — either surface expired token state in the UI or remove the method/enum.
- **N4:** `ConnectorAuthType.ApiKey` branch in `ConnectorCard.razor` shows "Managed" badge. No ApiKey connector exists currently, but this will silently prevent any future ApiKey connector from showing a "Configure" UI. Consider adding a distinct branch or documenting the intent.

---

## Positive Observations

- **User isolation in `RevokeConnectionAsync` is correct** — entraOid → user.Id lookup ensures a user can only delete their own tokens. The unique index `ix_mcp_user_tokens_user_server` provides an extra DB-level enforcement layer.
- **`IDbContextFactory` usage is correct** — all three service methods use `await using var db = ...`, properly disposing each context. The scoped-service + factory pattern is valid.
- **DB schema alignment is clean** — EF entity property names, `HasColumnName()` mappings, and actual migration columns all match. No snake_case drift issues (the recurring risk in this codebase).
- **AuthType.None → Managed badge logic** correctly prevents Connect/Revoke from appearing for service-managed connectors like forge-kb and search.
- **`HandleRevoke` correctly refreshes the grid** after a successful revoke via `await LoadConnectors()`, no stale state.
- **Loading/error/empty state trifecta** is all present in Connectors.razor — good defensive rendering pattern.

---

## What to Fix Before Re-Review

Tony, three things need to change:

**1. Fix Program.cs seed** — change `AuthType = "oauth_entra"` to `AuthType = "none"` for forge-kb, and add an `AuthType` correction to the update path so existing rows get fixed on next deploy.

**2. Fix null entraOid handling** in `Connectors.razor`:
  - `LoadConnectors()`: set `_error` when OID is null instead of silent skip
  - `HandleRevoke()`: add `Snackbar.Add(...)` error before returning

**3. Fix `ConnectorOAuthModal`** — either refactor to use `IDialogService` (preferred), or if inline rendering is intentional for this "coming soon" placeholder, document it as temporary and add a CSS overlay backdrop manually.

Items N1–N4 are not blocking but worth addressing in a follow-up if scope allows.

---

_Hawkeye — Cycle 1 complete. 3 critical, 2 important, 4 nitpicks._
