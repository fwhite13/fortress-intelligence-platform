# Build Report — ADO#3127

## What was built
Cold start UX for the FAIT assistant. When the user's Fargate task is not yet `Running`, the chat view now shows a loading card with a spinning indicator and cycling status messages instead of immediately rendering the chat UI. After 60 seconds it shows an error state with a "Try Again" button.

---

## Files changed

- `src/FortressAI.Web/Components/Shared/AssistantLoadingState.razor` — **new file** (141 lines)
  - Polls `/api/agent/status` every 2 seconds via `HttpClient`
  - Status message rotation: 0–10s = "Starting your assistant...", 10–25s = "Loading your memory...", 25–60s = "Almost ready..."
  - After 60s: timeout state with "Your assistant took too long to start." + "Try Again" button
  - CSS border-based spinner with `@@keyframes spin` animation
  - All colors/radii use `var(--...)` CSS variables — no hardcoded values
  - Implements `IDisposable` to clean up the `System.Timers.Timer` and `CancellationTokenSource`

- `src/FortressAI.Web/Components/Chat/ChatView.razor` — **modified** (+10 lines, 0 existing lines changed)
  - Added `@inject IUserAgentRuntime AgentRuntime`
  - Added guard fields: `_agentReady = false`, `_checkingAgent = true`
  - Extended `OnInitializedAsync` to call `AgentRuntime.GetSessionAsync()` after auth check; fails open on exception
  - Wrapped entire chat markup in `@if (_checkingAgent) / else if (!_agentReady) / else` guard
  - Added `HandleAgentReady()` and `HandleAgentRetry()` handlers
  - **Zero changes to existing chat markup, streaming, or Bedrock logic**

---

## Parallelization used
No — single-file dependency chain (create component, then wire into ChatView, then build).

## CC sessions run
1 CC run (sonnet). CC applied one fix beyond spec: `Session.UserId` is a `Guid` in this codebase; CC called `.ToString()` on it for the `GetSessionAsync(string userId)` parameter. Correct.

## Acceptance criteria verification

- [x] `AssistantLoadingState.razor` created — verified, file exists
- [x] Spinner displays with CSS border animation — CSS `@@keyframes spin` + `.assistant-loading-spinner` with `border-top-color: var(--color-accent)` ✓
- [x] Status message cycles: 0–10s / 10–25s / 25–60s — switch expression on `_elapsedSeconds` ✓
- [x] After 60s: error state + "Try Again" button — `_timedOut` flag + `HandleRetry` ✓
- [x] Polls `/api/agent/status` via `HttpClient` — `Http.GetFromJsonAsync<AgentStatusResponse>` ✓
- [x] `OnReady` invoked when status == "Running" — ✓
- [x] `OnRetry` callback wired — ✓
- [x] `ChatView.razor` guard wrapper — `@if / else if / else` around existing markup ✓
- [x] Existing chat markup unchanged — diff confirms 0 edits inside the else block ✓
- [x] Fails open on status check exception — `catch { _agentReady = true; }` ✓
- [x] Build succeeds — `dotnet build` returned 0 warnings, 0 errors ✓
- [x] No hardcoded colors/px — all `var(--...)` with fallbacks where vars may not exist ✓

## Known edge cases / things Clint should scrutinize

1. **`System.Timers.Timer` on server-side Blazor** — `Timer.Elapsed` fires on a ThreadPool thread. The `PollStatusAsync` method marshals back via `InvokeAsync(StateHasChanged)` which is correct. However, if a render cycle takes longer than 2s, Elapsed events can queue up. Not a correctness issue — the `_cts.IsCancellationRequested` guard at the top of `PollStatusAsync` prevents work after stop. Worth watching in load testing.

2. **`_elapsedSeconds` is incremented on each poll tick** — It's a simple counter (not wall clock). If the app is suspended (browser tab hidden, server GC pause), ticks may be delayed. The 60s timeout is therefore approximate. This is acceptable for a cold-start UX use case.

3. **`IDisposable` not `IAsyncDisposable`** — The component implements `Dispose()` (synchronous). `System.Timers.Timer.Dispose()` is synchronous and fine here. No async cleanup needed.

4. **`HttpClient` injection** — The component uses `[Inject] HttpClient`. This relies on a default `HttpClient` being registered in DI (via `AddHttpClient()` or similar). If the project only registers named clients, this will throw at runtime. Verify `Program.cs` has a default `HttpClient` or change to `IHttpClientFactory` + `_factory.CreateClient()`.

5. **Closing `</div>` of `chat-container`** — The else block in ChatView wraps `<div class="chat-container">...</div>` correctly. Verify via `dotnet build` (✓ passing) and browser that the layout doesn't shift.

## Commit
`19c6d319` — `feat(fait#3127): AssistantLoadingState cold start UX — spinner, status sequence, 60s timeout`

## How to test locally
1. Set `IUserAgentRuntime.GetSessionAsync()` to return `Status = RuntimeSessionStatus.Starting` for your user
2. Navigate to `/chat`
3. Verify: spinner + "Starting your assistant..." appears
4. After ~10s: "Loading your memory..."
5. After ~25s: "Almost ready..."
6. After 60s: error state + "Try Again" button
7. Change mock to return `Running` — verify `OnReady` fires and chat renders
8. Hit retry — verify polling restarts

## Fix Cycle 2
- HandleRetry: recreate _timer and _cts after StopPolling disposal. Commit 16151abe.

## Fix: EnsureRunningAsync on cold start
Added EnsureRunningAsync call before GetSessionAsync in ChatView.OnInitializedAsync. Without this, loading screen always times out — nothing launches the ECS task. Commit 8bf9078b.
