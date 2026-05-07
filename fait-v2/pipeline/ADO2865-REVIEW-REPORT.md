# REVIEW REPORT — ADO#2865 — Google Stitch Design Agent
**Reviewer:** Clint Barton (Hawkeye) | **Cycle:** 1 | **Date:** 2026-05-07
**Commit:** `aa91a57` | **Branch:** `main`

---

## Verdict: NEEDS-CHANGES

One critical runtime bug (missing JS function) and one significant functional gap (DB session/artifact records never written) must be resolved before this can ship.

---

## CRITICAL — Must Fix

### C1: `downloadBase64` JS function missing — runtime crash on Download

**File:** `Components/Agent/DesignArtifactCard.razor:168`

```csharp
await JS.InvokeVoidAsync("downloadBase64", fileName, "text/html", base64);
```

`downloadBase64` does not exist anywhere in the codebase. `App.razor` loads only `blazor.server.js` and `MudBlazor.min.js`. There are no `.js` files in `wwwroot/`. Clicking the Download button will throw a `JSException` at runtime.

**Fix:** Create `wwwroot/js/app.js` with the `downloadBase64` function and load it in `App.razor`.

```js
window.downloadBase64 = function (fileName, mimeType, base64) {
    const a = document.createElement('a');
    a.href = 'data:' + mimeType + ';base64,' + base64;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
};
```

```html
<!-- App.razor, after MudBlazor script -->
<script src="js/app.js"></script>
```

---

## NEEDS-CHANGES — Functional Gaps

### N1: DB session/artifact records never written — models are orphaned

`DesignAgentSession` and `DesignAgentArtifact` are registered in `FaitV2DbContext` and have tables in Aurora (from `AddMcpTables`), but **nothing writes to them**. `DesignAgentView.SendPrompt` calls `SaveArtifactAsync` (S3 write only) but never inserts a `design_agent_sessions` or `design_agent_artifacts` row.

The `_currentSessionId` field in the view is used only as an S3 path segment — it has no corresponding DB row.

If the intent is to track sessions and artifacts for listing/retrieval in a future WI, this needs to be documented or the writes need to be added. If WI#2865 scope is "DB schema only, no writes yet", that should be called out explicitly in the build report. Either write the records or document the deferred scope.

### N2: `IsStitchAvailableAsync` health check is a no-op stub

**File:** `Services/DesignAgentService.cs:185-186`

```csharp
var stitchEndpoint = _config["Stitch:HealthEndpoint"];
if (string.IsNullOrEmpty(stitchEndpoint))
    return true; // Configured but no health endpoint — assume available

// Health check is best-effort; treat failures as unavailable
return await Task.FromResult(true);  // ← never actually calls the endpoint
```

When `Stitch:HealthEndpoint` IS configured, the method still returns `true` without calling it. The comment says "treat failures as unavailable" but there is no HTTP call, so failures can never be observed. Should either implement the health call or remove the `HealthEndpoint` config key and comment to avoid confusion.

---

## ADVISORY — Non-blocking

### A1: Duplicate `AgentPluginBadge` when Design Agent active

`ChatView.razor:26-28` renders `<AgentPluginBadge>` in the toolbar when Design Agent is selected. `DesignAgentView.razor:11` also renders its own `<AgentPluginBadge>` in its header. When Design Agent is active, the badge appears twice. One of the two should be removed.

### A2: `Stitch:GcpCredentialsConfigured` — string comparison (Tony's flag #3)

**File:** `Services/DesignAgentService.cs:175`

```csharp
if (!string.Equals(gcpCredentials, "true", StringComparison.OrdinalIgnoreCase))
```

The `OrdinalIgnoreCase` covers `"True"` / `"TRUE"` variants, but `"1"` or `"yes"` would not be treated as truthy. The idiomatic .NET approach is `_config.GetValue<bool>("Stitch:GcpCredentialsConfigured")` which handles `true`/`false`/`1`/`0` consistently. Recommend switching.

### A3: `GenerateFallbackHtmlAsync` — markdown fence stripping edge case (Tony's flag #1)

**File:** `Services/DesignAgentService.cs:213-218`

The three-pass strip (` ```html ` → ` ``` ` → trailing ` ``` `) handles the standard cases correctly. One gap: if CC appends explanatory text after the closing fence (e.g. `\`\`\`\nThis HTML does...`), the trailing strip won't catch it because `raw.EndsWith("```")` would be false. Since the system prompt explicitly says "no explanation", this is unlikely but technically possible. The `OrdinalIgnoreCase` check for ` ```html ` is correct and handles uppercase variants. Adequate for current use but worth noting.

### A4: `SendPrompt` exception swallowing — no logging

**File:** `Components/Agent/DesignAgentView.razor:494-497`

```csharp
catch (Exception)
{
    _turns.Add(new DesignTurn("assistant", "Something went wrong. Please try again.", null, string.Empty));
}
```

All exceptions are caught silently. If S3 upload fails or the service throws, there is no log entry. Should inject `ILogger` and log at `Error` level before showing the user-facing message.

### A5: S3 key — `userId` and `sessionId` not sanitized

**File:** `Services/DesignAgentService.cs:155-156`

```csharp
var safeName = string.Concat(artifactName.Split(System.IO.Path.GetInvalidFileNameChars()));
var key = $"workspaces/{userId}/artifacts/design/{sessionId}/{safeName}.html";
```

`artifactName` is sanitized but `userId` (Entra OID) and `sessionId` (GUID) are not. In practice both are GUID-format so path traversal is not exploitable, but the defense-in-depth pattern would be to validate/sanitize all path segments. Low risk, advisory only.

---

## Mandatory Rules Checklist

| Rule | Result |
|------|--------|
| `GuidFormat = MySqlGuidFormat.None` on MySQL CSB | PASS — `keyRingCsb` set correctly in `Program.cs:61`; pre-existing `DefaultConnection` pattern unchanged |
| varchar(36) for all GUID columns | PASS — `[MaxLength(36)]` on all GUID columns in both new models |
| No hardcoded colors/fonts in .razor files | PASS — all colors via `var(--color-*)`, fonts via `var(--font-size-*)` |
| Icon pixel overrides (`14px !important`) | INFO — consistent with pre-existing MudBlazor workaround pattern in `ChatView.razor` |
| No Cognito references | PASS |
| No `@{ var x = ... }` inside `@if/@else` blocks | PASS |
| MudBlazor base icons only (no Rounded/Sharp variants) | PASS — all icons are `Icons.Material.Filled.*` base |
| `IHttpClientFactory` named clients, no raw `HttpClient` | PASS — `"HarnessClient"` named client used throughout |
| EF DateTime columns use `datetime(6)` | PASS — `created_at`, `updated_at` in both models mapped via `HasColumnType("datetime(6)")` in `FaitV2DbContext` |

---

## Interface Verification: `DispatchToolCallAsync`

| Check | Result |
|-------|--------|
| Added to `IUserAgentRuntime` | PASS — `IUserAgentRuntime.cs:23` |
| Implemented in `FargateUserAgentRuntime` | PASS — `FargateUserAgentRuntime.cs:354`, POSTs to `/tools/{toolName}` |
| Signature matches `DesignAgentService` call sites | PASS — `(string userId, string toolName, Dictionary<string, object> args, CancellationToken ct)` consistent |
| Any other `IUserAgentRuntime` implementations? | PASS — only `FargateUserAgentRuntime`; no mocks/stubs in codebase |

---

## File-by-File Summary

| File | Verdict | Notes |
|------|---------|-------|
| `Services/IDesignAgentService.cs` | PASS | Interface and `DesignAgentResult` record clean |
| `Services/DesignAgentService.cs` | NEEDS-CHANGES | N2 (health stub), A2 (string comparison), A3 (fence strip), A4 n/a here — see view |
| `Services/IUserAgentRuntime.cs` | PASS | `DispatchToolCallAsync` correctly added |
| `Services/FargateUserAgentRuntime.cs` | PASS | Implementation correct, named client used |
| `Data/Models/DesignAgentSession.cs` | PASS | MaxLength(36) on all GUIDs, EF config in DbContext |
| `Data/Models/DesignAgentArtifact.cs` | PASS | Same |
| `Data/FaitV2DbContext.cs` | PASS | `datetime(6)` on all timestamps, FK relationships correct |
| `Models/ActiveAgent.cs` | PASS | Clean enum |
| `Components/Agent/AgentPluginBadge.razor` | PASS (minor A1) | All CSS vars, no hardcoded colors |
| `Components/Agent/DesignArtifactCard.razor` | FAIL — C1 | `downloadBase64` JS missing |
| `Components/Agent/DesignAgentView.razor` | NEEDS-CHANGES | N1 (no DB writes), A1 (badge dupe), A4 (exception swallow) |
| `Components/Chat/ChatView.razor` | NEEDS-CHANGES | A1 (badge dupe) |
| `Program.cs` | PASS | DI registration correct |

---

## Required Actions Before Re-Review

1. **[CRITICAL]** Add `wwwroot/js/app.js` with `downloadBase64` function; load it in `App.razor` — fixes C1
2. **[REQUIRED]** Either: (a) implement `design_agent_sessions` + `design_agent_artifacts` DB writes in `DesignAgentView` / `DesignAgentService`, or (b) explicitly document in the build report that DB writes are deferred to a follow-up WI and the current scope is schema-only — fixes N1
3. **[REQUIRED]** Fix or remove the `IsStitchAvailableAsync` health endpoint stub — fixes N2
4. **[ADVISORY]** Remove one of the two duplicate `AgentPluginBadge` instances — fixes A1
