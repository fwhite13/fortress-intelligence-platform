# BUILD REPORT — ADO#2865 — Google Stitch Design Agent
**Sprint 3 | FAIT v2 Epic #2835 | §6.3 Design Agent**
**Agent:** Tony Stark | **Cycle:** 2 (review fixes) | **Date:** 2026-05-07

---

## Build Status: SUCCEEDED ✅

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.05
```

**Commit (cycle 1):** `aa91a57`
**Commit (cycle 2):** `3ca547d`
**Branch:** `main`
**Pushed to:** `origin/main`

---

## Cycle 2 — Review Fixes (Clint NEEDS-CHANGES)

### Fix 1 (CRITICAL): `downloadBase64` JS function

- Created `wwwroot/js/app.js` with `window.downloadBase64(fileName, mimeType, base64String)` — triggers browser download via `<a>` element
- Added `<script src="/js/app.js"></script>` to `Components/App.razor` before `</body>`
- Parameter order in JS matches the C# `JS.InvokeVoidAsync("downloadBase64", fileName, "text/html", base64)` call

### Fix 2 (CRITICAL): `DesignAgentService` DB persistence

- Injected `IDbContextFactory<FaitV2DbContext>` into `DesignAgentService`
- `GenerateScreenAsync`: creates and persists a `DesignAgentSession` record before generation; returns `SessionId` in `DesignAgentResult`
- `SaveArtifactAsync`: added optional `stitchScreenId`/`isFallback` params; persists a `DesignAgentArtifact` record after S3 upload
- `IDesignAgentService`: updated `SaveArtifactAsync` signature and added `SessionId` field to `DesignAgentResult` record
- `DesignAgentView.razor`: uses `result.SessionId` (falls back to `_currentSessionId`) and passes `result.ScreenId`/`result.IsFallback` to `SaveArtifactAsync`

### Fix 3 (CRITICAL): `IsStitchAvailableAsync` — no fake health check

- Removed the dead health endpoint branch that always returned `Task.FromResult(true)`
- Now: `return Task.FromResult(configured == "true")` — simple config check, no fake HTTP
- Failures on actual Stitch calls already caught and fallen back via existing try/catch in `GenerateScreenAsync`

### Fix 4 (IMPORTANT): Error logging in `SendPrompt`

- Added `[Inject] ILogger<DesignAgentView>` to `DesignAgentView.razor`
- `catch (Exception ex)` now logs: `Logger.LogError(ex, "SendPrompt failed for userId={UserId}", _userId)`

---

## What Was Built (Cycle 1)

### 1. `IUserAgentRuntime` + `FargateUserAgentRuntime` — DispatchToolCallAsync

- Added `Task<string> DispatchToolCallAsync(...)` to `IUserAgentRuntime.cs`
- Stubbed in `FargateUserAgentRuntime.cs`: POSTs JSON body to `http://{privateIp}:{port}/tools/{toolName}`, returns raw result string
- Enables WI#2866 (Stitch MCP wiring) to plug in tool calls via the harness endpoint

### 2. `IDesignAgentService` + `DesignAgentService`

- `Services/IDesignAgentService.cs`: interface with `GenerateScreenAsync`, `ExtractDesignContextAsync`, `RefineScreenAsync`, `SaveArtifactAsync`, `IsStitchAvailableAsync`
- `DesignAgentResult` record: `(Html, ScreenId, ProjectId, IsFallback)`
- `Services/DesignAgentService.cs`: full implementation
  - Dispatches `stitch_generate_screen`, `stitch_extract_design_dna`, `stitch_refine_screen` tool calls via `DispatchToolCallAsync`
  - Falls back to CC-native HTML generation via `SendTurnAsync` when Stitch unavailable
  - `IsStitchAvailableAsync` checks `Stitch:GcpCredentialsConfigured` config
  - Artifacts saved to S3 at `workspaces/{userId}/artifacts/design/{sessionId}/{name}.html`
- Registered as `AddScoped<IDesignAgentService, DesignAgentService>()` in `Program.cs`

### 3. DB Models (already migrated via `AddMcpTables`)

- `Data/Models/DesignAgentSession.cs`: `design_agent_sessions` — id, user_id, conversation_id, stitch_project_id, design_dna, timestamps
- `Data/Models/DesignAgentArtifact.cs`: `design_agent_artifacts` — id, session_id, user_id, artifact_name, s3_key, stitch_screen_id, is_fallback, created_at
- Both registered in `FaitV2DbContext` (`OnModelCreating` config matches existing snapshot — no new migration needed, tables already created in `AddMcpTables`)

### 4. Blazor Components

**`Components/Agent/AgentPluginBadge.razor`**
- Props: `AgentName` (string), `IsActive` (bool)
- Renders colored badge using `--color-accent-bg` / `--color-accent` CSS variables when active

**`Components/Agent/DesignArtifactCard.razor`**
- Displays iframe thumbnail (scaled 50%), artifact name, fallback tag ("CC-native")
- "Preview" button → fires `OnOpenPreview` callback (expands preview panel in parent)
- "Download" button → triggers browser file download via `IJSRuntime` `downloadBase64`

**`Components/Agent/DesignAgentView.razor`**
- Full chat UI: prompt input, image upload (via `InputFile`), turn history
- Calls `GenerateScreenAsync` on first prompt, `RefineScreenAsync` for follow-ups (uses last Stitch screenId)
- Image upload triggers `ExtractDesignContextAsync` → `designDnaContext` passed to generation
- Shows inline preview panel (`<iframe srcdoc>`) when "Open in Preview" clicked
- Shows "Stitch unavailable — using CC-native generation" notice when `IsFallback = true`
- All artifacts auto-saved to S3 after generation
- CSS animations: `@keyframes design-spin` for generating spinner

### 5. `ChatView.razor` — Agent Selector

- Added `ActiveAgent` enum (`MainAssistant | DesignAgent`) in `Models/ActiveAgent.cs`
- Agent selector toolbar at top: two pill buttons (Assistant, Design Agent)
- When `DesignAgent` active: renders `<DesignAgentView />` instead of message list, shows `<AgentPluginBadge>`
- When `MainAssistant` active: existing chat flow unchanged

---

## Acceptance Criteria Status

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `IDesignAgentService` and `DesignAgentService` implemented and registered | ✅ |
| 2 | `DispatchToolCallAsync` added to `IUserAgentRuntime` and stubbed in `FargateUserAgentRuntime` | ✅ |
| 3 | `design_agent_sessions` and `design_agent_artifacts` DB tables via EF migration | ✅ (in `AddMcpTables`) |
| 4 | `DesignAgentView.razor` renders: prompt → `GenerateScreenAsync` → iframe preview | ✅ |
| 5 | Image upload triggers `ExtractDesignContextAsync` before generation | ✅ |
| 6 | `DesignArtifactCard.razor` with Download + Preview buttons | ✅ |
| 7 | `AgentPluginBadge.razor` in chat header when Design Agent active | ✅ |
| 8 | Fallback to CC-native HTML with visible notice | ✅ |
| 9 | Artifacts saved to S3 `workspaces/{userId}/artifacts/design/{sessionId}/` | ✅ |
| 10 | Design Agent invokable from chat toolbar in `ChatView.razor` | ✅ |
| 11 | ALL styling via CSS variables — zero hardcoded colors/fonts/sizes | ✅ |

---

## Cycle 2 — Files Changed

| File | Action |
|------|--------|
| `wwwroot/js/app.js` | Created — `downloadBase64` JS helper |
| `Components/App.razor` | Modified — added `<script src="/js/app.js">` |
| `Services/IDesignAgentService.cs` | Modified — `SaveArtifactAsync` signature, `SessionId` on `DesignAgentResult` |
| `Services/DesignAgentService.cs` | Modified — DB factory injection, session/artifact persistence, `IsStitchAvailableAsync` fix |
| `Components/Agent/DesignAgentView.razor` | Modified — `ILogger` inject, `result.SessionId` usage, error logging |

---

## Cycle 1 — Files Changed

| File | Action |
|------|--------|
| `Services/IUserAgentRuntime.cs` | Modified — added `DispatchToolCallAsync` |
| `Services/FargateUserAgentRuntime.cs` | Modified — implemented `DispatchToolCallAsync` |
| `Services/IDesignAgentService.cs` | Created |
| `Services/DesignAgentService.cs` | Created |
| `Data/Models/DesignAgentSession.cs` | Created |
| `Data/Models/DesignAgentArtifact.cs` | Created |
| `Data/FaitV2DbContext.cs` | Modified — registered design agent entities |
| `Components/Agent/AgentPluginBadge.razor` | Created |
| `Components/Agent/DesignArtifactCard.razor` | Created |
| `Components/Agent/DesignAgentView.razor` | Created |
| `Components/Chat/ChatView.razor` | Modified — agent selector toolbar |
| `Models/ActiveAgent.cs` | Created |
| `Program.cs` | Modified — `AddScoped<IDesignAgentService, DesignAgentService>()` |

---

## Notes

- `design_agent_sessions` and `design_agent_artifacts` tables were pre-created in the `AddMcpTables` migration (20260507125357) by the Sprint 3 WI#2887 agent. EF model snapshot already reflects both tables. No additional migration was needed.
- `AgentPluginBadge.razor` and `DesignArtifactCard.razor` were also partially pre-staged by WI#2887 agent (residual CC artifacts, commit `b95d55d`). This WI completed the full implementations.
- WI#2866 (Stitch MCP harness wiring) remains a dependency for live Stitch integration. Until `Stitch:GcpCredentialsConfigured=true` is set, the service will use CC-native HTML fallback.

---

## Cycle 2 — Review Fixes (2026-05-07)

**Clint's verdict:** NEEDS-CHANGES (3 critical, 1 important)
**Cycle 2 commit:** `3ca547d`
**Build:** SUCCEEDED — 0 errors, 0 warnings

### Fixes Applied

| Fix | Issue | Resolution | Status |
|-----|-------|------------|--------|
| 1 (CRITICAL) | `downloadBase64` JS missing — runtime crash on download | Created `wwwroot/js/app.js` with `window.downloadBase64`; referenced via `<script src="/js/app.js">` in `App.razor` | ✅ |
| 2 (CRITICAL) | `DesignAgentService` never wrote to DB | Injected `IDbContextFactory<FaitV2DbContext>`; session persisted in `GenerateScreenAsync` before generation; artifact persisted in `SaveArtifactAsync` after S3 upload; `sessionId` threaded through `DesignAgentResult` | ✅ |
| 3 (CRITICAL) | `IsStitchAvailableAsync` always returned `true` without HTTP call | Replaced with Option A: config-based check — `_config["Stitch:GcpCredentialsConfigured"] == "true"` | ✅ |
| 4 (IMPORTANT) | Silent catch in `SendPrompt` swallowed exceptions | Added `Logger.LogError(ex, "SendPrompt failed for userId={UserId}", _userId)` in catch block | ✅ |

### Files Changed in Cycle 2

| File | Change |
|------|--------|
| `wwwroot/js/app.js` | Created — `window.downloadBase64` browser download helper |
| `Components/App.razor` | Added `<script src="/js/app.js"></script>` before `</body>` |
| `Services/DesignAgentService.cs` | Added `IDbContextFactory` injection; DB persistence in `GenerateScreenAsync` and `SaveArtifactAsync`; `IsStitchAvailableAsync` fixed to config check |
| `Services/IDesignAgentService.cs` | Updated `SaveArtifactAsync` signature to include `sessionId` parameter |
| `Components/Agent/DesignAgentView.razor` | Added `Logger.LogError` in `SendPrompt` catch; wires `result.SessionId` to artifact save |

### Cycle 2 Verification

```
dotnet build → Build succeeded. 0 Warning(s). 0 Error(s).
```

All four Clint review items addressed. Ready for Clint re-review.
