# Review Report — ADO#3127

### Verdict: NEEDS-CHANGES

---

### CC Review Summary

CC was invoked via claude CLI but was killed by SIGKILL (likely an OOM/timeout in the container). Fell back to direct adversarial analysis using the built files and targeted grep. Build was verified independently. All checklist items investigated manually.

---

### Spec Compliance Check

**§2 Files changed:**
- `src/FortressAI.Web/Components/Shared/AssistantLoadingState.razor` — ✅ created
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — ✅ modified (+10 lines, 0 existing lines changed)

**§6 Out of Scope:**
- ✅ No out-of-scope changes detected. Zero other files modified.

**§7 Acceptance Criteria:**
- [x] `AssistantLoadingState.razor` created — ✅
- [x] Spinner with CSS border animation — ✅ `@keyframes spin` + `.assistant-loading-spinner`
- [x] Status message cycles 0–10s / 10–25s / 25–60s — ✅ switch expression on `_elapsedSeconds`
- [x] 60s timeout → error state + "Try Again" button — ✅ `_timedOut` flag + `HandleRetry`
- [x] Polls `/api/agent/status` via `HttpClient` — ✅ `GetFromJsonAsync<AgentStatusResponse>`
- [x] `OnReady` invoked when status == "Running" — ✅
- [x] `OnRetry` callback wired — ✅ `HandleRetry` calls `OnRetry.InvokeAsync()`
- [x] `ChatView.razor` guard wrapper — ✅
- [x] Existing chat markup unchanged — ✅ confirmed in file
- [x] Fails open on status check exception — ✅ `catch { _agentReady = true; }`
- [x] Build passes — ✅ 0 errors, 0 warnings

**Spec compliance verdict:** ✅ COMPLIANT — but implementation has a functional bug in the retry path (see Issues #1).

---

### Consistency Audit

**Files cross-referenced:**
- `AssistantLoadingState.razor` ↔ `ChatView.razor` — `OnReady`/`OnRetry` callbacks match ✅
- `ChatView.razor` `GetSessionAsync(Session.UserId.ToString())` ↔ `UserSessionService.UserId` (Guid) ↔ `FargateUserAgentRuntime.GetSessionAsync(string userId)` ↔ `UserSession.UserId` (string) ✅
  - `Program.cs` line 232 confirms `NameIdentifier` claim is set to `user.Id.ToString()` — same format as what `ChatView` passes. Consistent.
- `AssistantLoadingState.razor` → `/api/agent/status` ↔ `Program.cs` line 509 endpoint ↔ `FargateUserAgentRuntime.GetSessionAsync` ✅
  - Response shape: `{ status: string }` — matches `AgentStatusResponse.Status` ✅

**Undocumented dependencies:**
- `IUserAgentRuntime` registered as singleton in `Program.cs` line 265 — already present, no new registration needed ✅

---

### Issues Found

| Severity | File | Location | Issue | Fix |
|----------|------|----------|-------|-----|
| Important | `AssistantLoadingState.razor` | `HandleRetry()` | Timer is null after timeout — polling never restarts on retry | Recreate `_timer` in `HandleRetry` |
| Nitpick | `AssistantLoadingState.razor` | `HandleRetry()` | Old `_cts` not disposed before replacement | Call `_cts?.Dispose()` before reassigning |
| Nitpick | `AssistantLoadingState.razor` | `PollStatusAsync()` | `Nav.BaseUri` used to build API URL — fragile behind reverse proxy | Use relative URL `/api/agent/status` directly |

---

### Issue Detail

#### I1 (Important): Timer null after timeout — "Try Again" button is broken

**Root cause:** `StopPolling()` disposes `_timer` and sets `_timer = null`. When the user clicks "Try Again", `HandleRetry()` calls `_timer?.Stop()` and `_timer?.Start()` — but `_timer` is null, so both are no-ops. Polling never resumes. The component resets its visual state (shows spinner + "Starting...") but nothing polls the backend. The user is stuck on a spinner forever.

This code path is triggered by the primary user-facing error recovery action (timeout → click Try Again). It's a silent failure.

**Fix:**

```diff
 private async Task HandleRetry()
 {
     _timedOut = false;
     _elapsedSeconds = 0;
     _statusMessage = "Starting your assistant...";
     _cts?.Cancel();
+    _cts?.Dispose();
     _cts = new CancellationTokenSource();
-    _timer?.Stop();
-    _timer?.Start();
+    _timer?.Stop();
+    _timer?.Dispose();
+    _timer = new System.Timers.Timer(2000);
+    _timer.Elapsed += async (_, _) => await PollStatusAsync();
+    _timer.AutoReset = true;
+    _timer.Start();
     StateHasChanged();
     await OnRetry.InvokeAsync();
 }
```

This also resolves I2 (dispose old CTS before replacement).

---

### Spec Fidelity

Implementation meets the spec intent. The AC bullet "After 60s: error state + Try Again button" implies the retry restores polling — that's the expected behavior and it's broken. The fix is small and isolated to `HandleRetry`.

---

### PASS items confirmed

- **CSS variable compliance:** All color properties use `var(--)`. `3px` border-width is structural (acceptable). Spacing/size values use `var(--x, fallback)` patterns throughout. ✅
- **Timer disposal in `Dispose()`:** `StopPolling()` cancels `_cts`, then `_cts?.Dispose()` follows. Clean. ✅
- **Thread marshaling in `PollStatusAsync`:** Every `StateHasChanged()` call goes through `InvokeAsync`. No direct `StateHasChanged()` on ThreadPool thread. ✅
- **Null safety on `GetFromJsonAsync`:** `response?.Status` is null-conditional. `_cts?.Token ?? CancellationToken.None` is safe. ✅
- **`OperationCanceledException` handling:** Caught by `catch (Exception)` — correct here; after cancel the timer is already stopped so no further polls fire. ✅
- **`ChatView.razor` markup integrity:** The `else` block correctly wraps `<div class="chat-container">...</div>` with a matching closing `}`. Blazor compile confirms (0 errors). ✅
- **`HandleAgentReady()` state transition:** Sets `_agentReady = true`, calls `StateHasChanged()` — invoked from EventCallback, so on the Blazor sync context. Correct. ✅
- **`HandleAgentRetry()` in ChatView:** Resets `_checkingAgent = true`, `_agentReady = false`, re-runs full status check. Correct behavior, no double-render risk. ✅
- **`Session.UserId.ToString()` format:** Consistent with `Program.cs` line 232 (`user.Id.ToString()` for NameIdentifier claim) and `UserSession.UserId` (string). ✅
- **Scope:** Zero changes outside the two specified files. ✅
- **Build:** `dotnet build` — 0 errors, 0 warnings. ✅

---

### What Tony needs to fix

**One change, isolated to `HandleRetry()` in `AssistantLoadingState.razor`:**

The timer is disposed by `StopPolling()` (called at timeout). `HandleRetry` must recreate it. Replace the current `HandleRetry` body with:

```csharp
private async Task HandleRetry()
{
    _timedOut = false;
    _elapsedSeconds = 0;
    _statusMessage = "Starting your assistant...";
    _cts?.Cancel();
    _cts?.Dispose();
    _cts = new CancellationTokenSource();
    _timer?.Stop();
    _timer?.Dispose();
    _timer = new System.Timers.Timer(2000);
    _timer.Elapsed += async (_, _) => await PollStatusAsync();
    _timer.AutoReset = true;
    _timer.Start();
    StateHasChanged();
    await OnRetry.InvokeAsync();
}
```

That's the entire fix. No other files need changing. Build will still pass clean.

---

_Review by Hawkeye — cycle 1_
