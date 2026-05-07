# Review Report: ADO#2892 — FAIT v2: Cold Start Loading Screen

**Reviewer:** Hawkeye (Code Reviewer)
**Date:** 2026-05-07
**Commit:** `35b23f2`
**Verdict:** ✅ **PASS**

---

## Spec Compliance Check

**§2 Codebase Map:**
- `src/FortressAI.V2.Web/Components/Agent/AssistantLoadingState.razor` — ✅ created
- `src/FortressAI.V2.Web/Components/Pages/Dashboard.razor` — ✅ modified (gates on `_agentReady`)
- `Program.cs` — ✅ modified (`/api/agent/status` stub added)
- `app.css` — ✅ modified (loading screen styles added)

**§6 Out of Scope:** ✅ No out-of-scope changes detected

**§7 Acceptance Criteria:**
- [x] Cold start shows loading screen before Dashboard renders — ✅ Dashboard gates on `_agentReady`
- [x] Polls `/api/agent/status` on 2s interval — ✅ Timer fires at 2s, repeats at 2s
- [x] Timeout at 60s with retry option — ✅ `MaxPolls = 30`, 30 × 2s = 60s; Retry button present
- [x] Status messages cycle during wait — ✅ `_pollCount` switch drives three message phases
- [x] Transitions to Dashboard on ready signal — ✅ `OnReady.InvokeAsync()` called on success response

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Files Cross-Referenced:**
- `AssistantLoadingState.razor` ↔ `Dashboard.razor` — ✅ `OnReady` callback wires correctly
- `AssistantLoadingState.razor` ↔ `Program.cs` — ✅ polls `/api/agent/status`, stub present
- `app.css` ↔ `AssistantLoadingState.razor` — ⚠️ `.loading-fade-out` defined in CSS, never applied in razor (see N1)

**Undocumented Dependencies:** None found

---

## Critical Issues — 0

None.

---

## Important Issues — 0

None.

---

## Nitpicks — 2

### N1: Dead CSS — `.loading-fade-out` defined but never applied

- **File:** `app.css` + `AssistantLoadingState.razor`
- **Category:** Cleanup debt
- **Issue:** `.loading-fade-out` is defined in `app.css` but grep confirms it is never referenced anywhere in `AssistantLoadingState.razor`. It appears to be prep for a fade animation that wasn't wired up.
- **Impact:** Zero — purely dead code. No runtime effect.
- **Fix:** Either apply the class when transitioning away (set a bool on ready and bind it), or delete the CSS rule. Not a blocker.

---

### N2: Spinner stays visible on timeout (additive error state)

- **File:** `src/FortressAI.V2.Web/Components/Agent/AssistantLoadingState.razor` (lines 4–7)
- **Category:** UX polish
- **Issue:** When `_timedOut = true`, the spinner `<div>` has no conditional guard — it renders unconditionally. The error state is purely additive: spinner + "Taking longer than expected." + retry button all show simultaneously. It would be cleaner to hide the spinner on timeout.
- **Evidence:**
  ```razor
  @* Fortress brand spinner — always rendered *@
  <div class="loading-spinner">
      <div class="loading-spinner-ring"></div>
  </div>
  ...
  @if (_timedOut)
  {
      <div class="loading-error-state">...</div>
  }
  ```
- **Impact:** Minor UX — spinning while showing a timeout message looks odd. Not broken.
- **Fix:**
  ```diff
  - <div class="loading-spinner">
  + <div class="loading-spinner" style="@(_timedOut ? "display:none" : "")">
  ```
  Or use a CSS class toggle. Not a blocker.

---

## Pre-Verified Critical Checks (clean)

| Check | Result |
|---|---|
| `IHttpClientFactory` used (not raw `HttpClient`) | ✅ |
| `MaxPolls = 30`, 2s interval = 60s timeout | ✅ |
| `Dashboard.razor` gates on `_agentReady` | ✅ |
| `/api/agent/status` stub in `Program.cs` | ✅ |
| Build: 0 errors, 0 warnings | ✅ |
| `InvokeAsync` used in timer callback | ✅ |
| `Dispose()` calls `_pollTimer?.Dispose()` | ✅ |

---

## Positive Observations

- Clean `IDisposable` implementation — no timer leak risk.
- `Retry()` correctly resets all state (`_timedOut`, `_pollCount`, `_statusMessage`) and reinstantiates the timer — no edge case lurking there.
- `InvokeAsync` wrapping the timer callback is correct Blazor threading practice. Tony got this right without prompting.
- `/api/agent/status` response parsing handles both `"Running"` and `"running"` casing — defensive and good.

---

## Summary

Clean build, correct implementation, all acceptance criteria met. Two nitpicks (dead CSS, cosmetic spinner-on-timeout overlap) but neither blocks ship. PASS.

---

_Hawkeye — Code Reviewer | ADO#2892 | Cycle 1_
