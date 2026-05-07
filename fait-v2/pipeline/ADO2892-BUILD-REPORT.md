# Build Report — ADO#2892
## FAIT v2: Cold Start Loading Screen — AssistantLoadingState UI

**Engineer:** Tony Stark  
**Commit:** `35b23f2`  
**Build:** ✅ SUCCEEDED — 0 errors, 0 warnings  
**Date:** 2026-05-07  

---

## What Was Built

Full-screen cold start loading state for the FAIT v2 dashboard. When a user's Fargate task is not
yet running, they see a branded spinner with cycling status messages instead of a blank screen.
Dashboard gates rendering of the main chat UI behind an `_agentReady` bool, flipping it via an
`OnReady` callback from the loading component.

---

## Files Changed

| File | Action | Notes |
|------|--------|-------|
| `Components/Agent/AssistantLoadingState.razor` | **Created** | New component with polling, 60s timeout, retry |
| `Components/Pages/Dashboard.razor` | **Modified** | Gates on `_agentReady`; shows loading vs chat |
| `Program.cs` | **Modified** | Added `GET /api/agent/status` stub (AllowAnonymous) |
| `Components/_Imports.razor` | **Modified** | Appended `@using FortressAI.V2.Web.Components.Agent` |
| `wwwroot/css/app.css` | **Modified** | Appended loading state CSS — all CSS variables, no hardcoded values |

---

## Parallelization Used

No — single sequential CC session. All 5 changes had dependencies (imports needed for component,
component needed for Dashboard, stub needed for polling to not error).

---

## CC Sessions Run

1 CC session (sonnet). Brief used `IHttpClientFactory` + `NavigationManager.BaseUri` pattern
instead of raw `HttpClient` injection — correct for Blazor Server where only a named client
`HarnessClient` was registered (factory is still available for unnamed clients).

---

## Acceptance Criteria Verification

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `AssistantLoadingState.razor` in `Components/Agent/` | ✅ Created at correct path |
| 2 | Polls `/api/agent/status` every 2 seconds | ✅ `TimeSpan.FromSeconds(2)` interval |
| 3 | Status messages cycle: "Starting..." → "Loading your memory..." → "Almost ready..." | ✅ Switch on `_pollCount` (≤3 / ≤7 / _) |
| 4 | Subtitle: "Your assistant remembers where you left off." | ✅ Hard-coded string in markup |
| 5 | 60-second timeout → error state with retry button | ✅ `MaxPolls = 30`, timer fired at 2s intervals |
| 6 | Spinner uses `var(--color-primary)` — no hardcoded colors | ✅ All CSS uses variables |
| 7 | Dashboard shows loading when `!_agentReady`, chat UI when `_agentReady` | ✅ Conditional render in Dashboard.razor |
| 8 | Stub `/api/agent/status` returns `{ "status": "Starting" }` | ✅ Minimal API, AllowAnonymous |
| 9 | `dotnet build` = 0 errors, 0 warnings | ✅ Verified |
| 10 | Commit message matches spec | ✅ `feat(fait-v2#2892): AssistantLoadingState cold start UI with polling and CSS spinner` |

---

## Design Notes / Things Clint Should Scrutinize

1. **`IHttpClientFactory` vs `HttpClient`** — Used factory pattern because Blazor Server doesn't
   support direct `HttpClient` injection without explicit `AddHttpClient()`. Factory creates
   an unnamed client; base address set from `NavigationManager.BaseUri` so the request stays
   within the same host. This is correct but Clint should verify the base URI construction
   produces a valid absolute URL in both dev and prod ECS environments.

2. **Timer in `OnInitialized` vs `OnAfterRenderAsync`** — Timer starts in `OnInitialized`. On
   Blazor Server, `InvokeAsync` will correctly dispatch back to the circuit's render context.
   No SSR/prerender issue here since `Dashboard.razor` doesn't use `@rendermode` — it inherits
   Interactive Server from the root. Confirm `App.razor` / `Routes.razor` set InteractiveServer
   globally.

3. **`_agentReady = false` always on load** — Per spec §14.4, this is intentional for this WI.
   Future WI wires `IUserAgentRuntime.GetStatusAsync()` to skip loading if already Running.
   Clint should confirm the stub doesn't return `Running` (it returns `Starting`, so loading
   always shows — as designed).

4. **`AllowAnonymous` on `/api/agent/status`** — The component polls this before auth state is
   fully resolved in some edge cases. Made it public to avoid 401 spam. A future WI can add
   user-scoped status when `IUserAgentRuntime` is wired in.

5. **Timer disposal** — `Dispose()` calls `_pollTimer?.Dispose()`. Also disposes inside the
   timer callback on timeout and on `OnReady`. Belt-and-suspenders is fine; no double-dispose
   issue with `Timer` as second dispose is a no-op.

---

## How to Test Locally

```bash
cd ~/projects/fip/fait-v2
dotnet run --project src/FortressAI.V2.Web/

# Navigate to http://localhost:5000
# You should see the loading spinner (agent never becomes "Running" with stub)
# Wait ~60 seconds — "Taking longer than expected." error state should appear
# Click "Try again" — timer resets and cycles start again
```

To test the ready transition manually, temporarily change the stub to return `"Running"`:
```csharp
app.MapGet("/api/agent/status", () => Results.Ok(new { status = "Running" })).AllowAnonymous();
```
Then refresh — should transition immediately to chat UI.

---

## Notes for Maria / Jarvis

- Build is clean and committed. Sending to Clint for review.
- Future WI: wire `IUserAgentRuntime.GetStatusAsync()` into Dashboard to skip loading when
  Fargate task is already running (avoids unnecessary 2s flash on quick refreshes).
- The `/api/agent/status` endpoint stub is scoped — the real implementation will replace it
  without any component changes needed.
