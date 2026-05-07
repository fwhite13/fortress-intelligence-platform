# Review Report — ADO#2865
## Google Stitch Design Agent — Service Layer & UI Components

**Reviewer:** Clint Barton (Hawkeye)
**Cycle:** 1
**Commit:** `aa91a57`
**Date:** 2026-05-07
**Verdict:** ⚠️ NEEDS-CHANGES

---

## Spec Compliance Check

**Brief:** `pipeline/review-2865-brief.md`
**Spec:** `memory/projects/fait-v2-spec-2026-04-27.md` §6.3, §14

### §Codebase Map — File Inventory

Tony's brief listed the following files. Actual diff includes:

| File | Status |
|------|--------|
| `Services/IDesignAgentService.cs` | ✅ Present |
| `Services/DesignAgentService.cs` | ✅ Present |
| `Data/Models/DesignAgentSession.cs` | ✅ Present |
| `Data/Models/DesignAgentArtifact.cs` | ✅ Present |
| `Models/ActiveAgent.cs` | ✅ Present |
| `Components/Agent/AgentPluginBadge.razor` | ✅ Present |
| `Components/Agent/DesignArtifactCard.razor` | ✅ Present |
| `Components/Agent/DesignAgentView.razor` | ✅ Present |
| `Services/IUserAgentRuntime.cs` | ✅ Modified — `DispatchToolCallAsync` added |
| `Services/FargateUserAgentRuntime.cs` | ✅ Modified — `DispatchToolCallAsync` stubbed |
| `Data/FaitV2DbContext.cs` | ✅ Modified — both DbSets registered |
| `Program.cs` | ✅ Modified — `IDesignAgentService` scoped |
| `Components/Chat/ChatView.razor` | ✅ Modified — agent selector added |

**Note:** The git diff for commit `aa91a57` shows only 5 files changed (5 actually in the commit). The remaining files listed by Tony exist in the working tree and appear consistent with prior work. All files are present and accounted for.

### §6.3 Spec Compliance — Design Agent Requirements

| Requirement | Status |
|-------------|--------|
| Stitch MCP dispatch via harness with CC-native HTML fallback | ✅ Both paths implemented |
| Artifacts saved to `workspaces/{userId}/artifacts/design/{session-id}/` | ✅ S3 key follows this pattern (§14 AWS-first) |
| User can request refinements in same thread (iterative) | ✅ `RefineScreenAsync` + `lastScreenId` detection in UI |
| `DispatchToolCallAsync` on `IUserAgentRuntime` | ✅ Interface updated, Fargate stub present |
| DB tables `design_agent_sessions` + `design_agent_artifacts` registered | ✅ DbSets + EF config present |

**Critical gap:** The spec says artifacts are saved to the user workspace. `SaveArtifactAsync` correctly writes to S3, but **no `DesignAgentSession` or `DesignAgentArtifact` DB record is written anywhere** — not in `SaveArtifactAsync`, not in `SendPrompt`, not in the service. The DB models are registered but orphaned. See C2 below.

**Spec compliance verdict:** ⚠️ PARTIALLY COMPLIANT — C1 and C2 must be resolved.

---

## Consistency Audit

### `DispatchToolCallAsync` Signature

| Location | Signature |
|----------|-----------|
| `IUserAgentRuntime.cs:23` | `Task<string> DispatchToolCallAsync(string userId, string toolName, Dictionary<string, object> args, CancellationToken ct = default)` |
| `FargateUserAgentRuntime.cs:354` | ✅ Matches exactly |
| `DesignAgentService.cs:58, 95, 128` | ✅ Called with matching parameter types |

**No other implementations of `IUserAgentRuntime`** found in the service layer — only `FargateUserAgentRuntime`. No mocks or test doubles present. Consistent.

### DB Fluent Config vs. Model Properties

`DesignAgentSession` and `DesignAgentArtifact` entity properties cross-checked against EF fluent config in `FaitV2DbContext`. All `HasColumnName`, `HasMaxLength`, `HasColumnType`, and `IsRequired` calls match the model annotations. No drift found.

### CSS Variables — Design Components

Spot-checked `DesignAgentView.razor` and `DesignArtifactCard.razor`. All color, spacing, font, and radius values use CSS custom properties (`var(--color-*)`, `var(--space-*)`, `var(--font-size-*)`, `var(--radius-*)`). No hardcoded hex/px color/font values found. ✅

### MudBlazor Icons

Checked all `Icons.Material.Filled.*` references in new components. No `Rounded`, `Sharp`, or `Outlined` variants used. Base icons only. ✅

---

## Critical Issues — 3 Found

### C1: `downloadBase64` JS function does not exist — runtime crash on Download

- **File:** `Components/Agent/DesignArtifactCard.razor` (line ~168)
- **Category:** Correctness / Runtime error
- **Issue:** `HandleDownload` calls `JS.InvokeVoidAsync("downloadBase64", fileName, "text/html", base64)`. This JS function does not exist anywhere in the application. `Components/App.razor` loads only `blazor.server.js` and `MudBlazor.min.js`. No custom `app.js` or `site.js` is present. Every click of the Download button will throw a JavaScript `TypeError` at runtime: `downloadBase64 is not defined`.
- **Evidence:**
  ```csharp
  // DesignArtifactCard.razor:168
  await JS.InvokeVoidAsync("downloadBase64", fileName, "text/html", base64);
  ```
  ```html
  <!-- App.razor — no custom JS file loaded -->
  <script src="_framework/blazor.server.js"></script>
  <script src="_content/MudBlazor/MudBlazor.min.js"></script>
  ```
- **Impact:** Download button is completely non-functional. The exception will bubble to the Blazor circuit — depending on error handling, this could display a generic error or crash the current SignalR connection.
- **Fix:** Add a `wwwroot/js/app.js` with the function and reference it in `App.razor`:
  ```javascript
  // wwwroot/js/app.js
  window.downloadBase64 = function (fileName, mimeType, base64) {
      const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
      const blob = new Blob([bytes], { type: mimeType });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
  };
  ```
  ```diff
  // App.razor
  + <script src="js/app.js"></script>
    <script src="_framework/blazor.server.js"></script>
  ```

---

### C2: `DesignAgentSession` and `DesignAgentArtifact` DB records never written — models orphaned

- **File:** `Services/DesignAgentService.cs` — `SaveArtifactAsync`; `Components/Agent/DesignAgentView.razor` — `SendPrompt`
- **Category:** Correctness / Missing implementation
- **Issue:** The DB models `DesignAgentSession` and `DesignAgentArtifact` are registered in `FaitV2DbContext` and their EF config is correct, but neither is ever written. `SaveArtifactAsync` puts the file in S3 only — no DB record. `SendPrompt` calls `SaveArtifactAsync` only. There is no DB context injection in `DesignAgentService`, no `_context.DesignAgentSessions.Add(...)`, no `SaveChangesAsync`. The tables `design_agent_sessions` and `design_agent_artifacts` will remain permanently empty.
  
  This means:
  - No artifact history is queryable
  - No session tracking exists (the `_currentSessionId` in the UI is a client-side GUID that dies with the component)
  - Future features (artifact gallery, Design ↔ BA handoff where artifacts must be attachable to submissions per §6.3) will have nothing to attach
- **Evidence:**
  ```csharp
  // DesignAgentService.cs — SaveArtifactAsync only touches S3
  public async Task<string> SaveArtifactAsync(...)
  {
      var key = $"workspaces/{userId}/artifacts/design/{sessionId}/{safeName}.html";
      await _s3.PutObjectAsync(new PutObjectRequest { ... }, ct);
      // No: _context.DesignAgentArtifacts.Add(...); await _context.SaveChangesAsync();
      return key;
  }
  ```
- **Fix:** Inject `FaitV2DbContext` into `DesignAgentService`. Create session record on first `GenerateScreenAsync` call (or when `DesignAgentView` initializes a session). Persist artifact record in `SaveArtifactAsync`:
  ```csharp
  // In SaveArtifactAsync, after S3 write:
  var artifact = new DesignAgentArtifact
  {
      SessionId = sessionId,
      UserId = userId,
      ArtifactName = artifactName,
      S3Key = key,
      IsFallback = isFallback  // pass through from caller
  };
  _context.DesignAgentArtifacts.Add(artifact);
  await _context.SaveChangesAsync(ct);
  ```
  For session creation, `DesignAgentView.razor` should call a new `IDesignAgentService.CreateSessionAsync(userId)` on `OnInitializedAsync` or the first generate call, storing the returned session ID in `_currentSessionId` (rather than generating a client-side GUID).

---

### C3: `IsStitchAvailableAsync` stub always returns `true` when health endpoint is configured

- **File:** `Services/DesignAgentService.cs` lines 171–188
- **Category:** Correctness / Misleading behavior
- **Issue:** When `Stitch:HealthEndpoint` is set in config, the health check returns `Task.FromResult(true)` unconditionally — no HTTP call is made. The comment says "treat failures as unavailable" but the code never issues a request, let alone handles a failure. In any non-dev environment where `Stitch:HealthEndpoint` is configured, Stitch will always be reported as available even if the endpoint is down, returning 500s, or unreachable.
- **Evidence:**
  ```csharp
  var stitchEndpoint = _config["Stitch:HealthEndpoint"];
  if (string.IsNullOrEmpty(stitchEndpoint))
      return true; // Configured but no health endpoint — assume available

  // Health check is best-effort; treat failures as unavailable
  return await Task.FromResult(true);  // ← stub, never calls the endpoint
  ```
- **Impact:** When Stitch is configured but down, all calls will dispatch to `DispatchToolCallAsync` rather than falling back to CC-native. This cascades as exception → catch → CC fallback, which works but logs `LogWarning` as an unexpected error instead of expected fallback, and adds latency from the failed Stitch call.
- **Note from Tony's brief #3:** Tony flagged `Stitch:GcpCredentialsConfigured` as potentially fragile. The string comparison (`string.Equals(..., "true", OrdinalIgnoreCase)`) is actually acceptable — the real problem is the unreachable health check stub.
- **Fix:** Either implement the HTTP health check or remove the `Stitch:HealthEndpoint` branch entirely (rely on exception → fallback path as the availability signal). For v1, removing the branch is simpler and honest:
  ```csharp
  public async Task<bool> IsStitchAvailableAsync(CancellationToken ct = default)
  {
      var gcpCredentials = _config["Stitch:GcpCredentialsConfigured"];
      if (!string.Equals(gcpCredentials, "true", StringComparison.OrdinalIgnoreCase))
      {
          _logger.LogDebug("Stitch unavailable: GCP credentials not configured");
          return false;
      }
      // No health endpoint implemented yet — assume available if credentials configured.
      // Actual availability will surface via exception in the call path.
      return await Task.CompletedTask.ContinueWith(_ => true, ct);
  }
  ```
  Or if a real health check is desired, use `IHttpClientFactory` named client to hit `stitchEndpoint` with a HEAD request and treat non-2xx as unavailable.

---

## Important Issues — 1 Found

### I1: `SendPrompt` catch block silently swallows all exceptions without logging

- **File:** `Components/Agent/DesignAgentView.razor` lines 494–497
- **Category:** Quality / Observability
- **Issue:**
  ```csharp
  catch (Exception)
  {
      _turns.Add(new DesignTurn("assistant", "Something went wrong. Please try again.", null, string.Empty));
  }
  ```
  Any exception — network timeout, S3 auth failure, Fargate connectivity issue, null ref in result processing — is silently consumed. No `Logger.LogError`, no exception details captured. When this fires in production, there will be no trace of what went wrong.
- **Fix:**
  ```csharp
  catch (Exception ex)
  {
      Logger.LogError(ex, "Design generation failed for user {UserId}", _userId);
      _turns.Add(new DesignTurn("assistant", "Something went wrong. Please try again.", null, string.Empty));
  }
  ```
  Inject `ILogger<DesignAgentView>` into the component (it likely already has it or can get it via `[Inject]`).

---

## Nitpicks — 2 Found

### N1: Duplicate `AgentPluginBadge` when Design Agent is active in `ChatView`

- `ChatView.razor` now renders an `AgentPluginBadge` for the active agent. `DesignAgentView.razor` also renders its own `AgentPluginBadge` in its header. If `ChatView` embeds `DesignAgentView` as a child component while also showing its own badge, users will see two overlapping badges. Not blocking — depends on how the routing between views works — but worth a visual QA pass.

### N2: Fallback HTML fence stripping misses post-fence appended text (Tony's flag #1)

- The current stripping logic handles ```` ```html ```, ` ``` ` prefix, and ` ``` ` suffix. It will not strip text that CC appends **after** the closing fence (e.g., "This HTML includes..."). Low risk per Tony — CC usually doesn't do this when instructed "return only HTML markup" — but the iframe `srcdoc` would contain stray text as a comment at minimum, or as visible DOM text if outside `<html>` tags. Non-blocking; monitor in testing.

---

## Mandatory Rules Check

| Rule | Status |
|------|--------|
| `GuidFormat = MySqlGuidFormat.None` on all MySQL connections | ✅ Not changed by this WI; existing connection builders unmodified |
| varchar(36) for all GUID columns in new EF models | ✅ `DesignAgentSession.Id`, `DesignAgentArtifact.Id` — both `HasMaxLength(36)` |
| EF DateTime columns use `datetime(6)` | ✅ `HasColumnType("datetime(6)")` on both `created_at` and `updated_at` in both models |
| No hardcoded colors/fonts/sizes in .razor files | ✅ All CSS in new components uses CSS variables only |
| No Cognito references | ✅ None |
| No `@{ var x = ... }` inside Razor `@if/@else` with markup | ✅ None observed |
| MudBlazor: base icons only, no `Rounded`/`Sharp`/`Outlined` variants | ✅ All icons use `Icons.Material.Filled.*` |
| `IHttpClientFactory` named clients — no raw `HttpClient` | ✅ No `HttpClient` instantiated in new code |
| All `IUserAgentRuntime` implementations have `DispatchToolCallAsync` | ✅ One implementation (`FargateUserAgentRuntime`) — confirmed present; no other implementations in codebase |
| `DispatchToolCallAsync` signature consistency | ✅ Interface, Fargate impl, and all callers are consistent |

---

## Positive Observations

- **Fallback architecture is solid.** The Stitch → CC-native fallback with user-visible notice (`_stitchUnavailableNotice`) is well-designed. Tony got this right.
- **Refinement detection is clean.** `lastScreenId` tracking in `SendPrompt` auto-switches to `RefineScreenAsync` without requiring a separate user action. Good UX.
- **EF fluent config is thorough.** Both new entities have complete fluent config with indexes, FKs, and correct column types. No guesswork columns.
- **FargateUserAgentRuntime stub is correct.** The stub throws `NotImplementedException` with a clear message rather than silently returning null. Will surface clearly when Stitch is actually dispatched.
- **`DesignAgentView` image upload and design DNA extraction flow** — the hand-off from image upload → `ExtractDesignContextAsync` → use as `designDnaContext` on next generation is a clean implementation of the §6.3 spec intent.

---

## What Tony Needs to Fix

Three changes required before PASS:

**1. Add `downloadBase64` JS function** (`DesignArtifactCard.razor` / `App.razor`)
- Create `wwwroot/js/app.js` with the `downloadBase64` window function (see C1 fix above)
- Add `<script src="js/app.js"></script>` to `App.razor` before framework scripts

**2. Write `DesignAgentSession` + `DesignAgentArtifact` DB records** (`DesignAgentService.cs`)
- Inject `FaitV2DbContext` into `DesignAgentService`
- Create session record on first generation call (or in a new `CreateSessionAsync` method called from UI)
- Persist `DesignAgentArtifact` in `SaveArtifactAsync` after S3 write, passing `IsFallback` from the caller

**3. Fix `IsStitchAvailableAsync` stub** (`DesignAgentService.cs`)
- Remove the `Task.FromResult(true)` stub that never calls the configured health endpoint
- Either implement the actual HTTP health check (preferred), or simplify by removing the `stitchEndpoint` branch and relying on exception-as-availability-signal

**Also fix (non-blocking but important):**
- Add `Logger.LogError(ex, ...)` in `DesignAgentView.SendPrompt` catch block (I1)

---

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| `IDesignAgentService` + `DesignAgentService` with Stitch dispatch and CC fallback | ✅ Implemented |
| `DesignAgentView.razor` with image upload, generation, refinement, fallback notice, preview panel | ✅ All present |
| `DesignArtifactCard.razor` with Download and Open in Preview buttons | ⚠️ Download crashes (C1) |
| `AgentPluginBadge.razor` | ✅ Present |
| `DispatchToolCallAsync` added to `IUserAgentRuntime` | ✅ Present |
| DB models registered in `FaitV2DbContext` | ✅ Registered; ⚠️ Never written (C2) |
| Agent selector in `ChatView.razor` | ✅ Present |
| No new DB migration needed (tables in AddMcpTables) | ✅ Confirmed |
| All FAIT v2 mandatory rules pass | ✅ All pass |
