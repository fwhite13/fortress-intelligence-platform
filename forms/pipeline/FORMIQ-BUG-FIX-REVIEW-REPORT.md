# Review Report: FormIQ Bug Fix Sprint

**Reviewer:** Hawkeye  
**Date:** 2026-02-27  
**Commits reviewed:** `6d0a6fb` (fix) + `df0b08d` (build report)  
**Priority:** HIGH

---

### Verdict: NEEDS-CHANGES

One critical bug — one fix needed, then this ships.

---

## Consistency Audit

**Files Cross-Referenced:**
- `FormDetail.razor` ↔ `FormsController.cs` — ✅ Route `/forms/{Id:int}` matches controller `[HttpGet("{id:int}")]`
- `FormDetail.razor` ↔ `FormDtos.cs` (`FormDetailDto`) — ✅ All properties accessed match the DTO definition; `Fields` initialized to `new()` (no null risk)
- `FormDetail.razor` ↔ `app.css` — ✅ CSS classes `.confidence-high`, `.confidence-medium`, `.confidence-low` exist at lines 87/96/105
- `FormLibrary.razor` ↔ `FormDetail.razor` — ✅ `MudLink Href="@($"/forms/{context.Id}")"` correctly resolves to `@page "/forms/{Id:int}"`
- `MainLayout.razor` ↔ `_theme.LayoutProperties.AppbarHeight` — ✅ `AppbarHeight = "64px"` confirms 80px (64+16) is correct

**Undocumented Dependencies:**
- None found.

---

## Critical Issues — 1

### C1: Missing `@implements IDisposable` — Timer Will Leak on Navigation

- **File:** `FortressFormTools.Web/Components/Pages/FormDetail.razor` (directives section, line 1–6)
- **Category:** Correctness — Blazor lifecycle
- **Issue:** `FormDetail.razor` defines a `public void Dispose()` method and creates a `System.Threading.Timer` for polling. However, the file is missing the `@implements IDisposable` directive. In Blazor, the framework only calls `Dispose()` when it can observe that the component implements `IDisposable` — it does this with `component is IDisposable` at teardown. Without the directive, the generated class does not implement the interface, and `Dispose()` becomes an unreachable dead method.

  **Result:** When a user navigates away from a form that is in `Queued` or `Processing` status, the 3-second timer keeps firing indefinitely, making `GET api/forms/{Id}` calls until the form eventually reaches a terminal status (or the circuit closes). In a stuck-processing scenario, the timer never self-stops.

- **Evidence:**
  ```razor
  @page "/forms/{Id:int}"
  @using System.Net.Http.Json
  @using FortressFormTools.Web.Models
  @inject HttpClient Http
  @inject NavigationManager Nav
  @inject ISnackbar Snackbar
  @* ← @implements IDisposable is MISSING *@
  ```
  ```csharp
  public void Dispose()          // ← never called by the framework
  {
      _pollTimer?.Dispose();
  }
  ```

- **Impact:** Timer leak + unnecessary API calls after navigation. In Blazor Server, `InvokeAsync` will keep dispatching to a component that is no longer rendered for the duration of the leak.

- **Fix — one line:**
  ```diff
  @page "/forms/{Id:int}"
  @using System.Net.Http.Json
  @using FortressFormTools.Web.Models
  @inject HttpClient Http
  @inject NavigationManager Nav
  @inject ISnackbar Snackbar
  +@implements IDisposable
  ```

---

## Important Issues — 0

None.

---

## Nitpicks — 2

- **N1: `publish/` directory not in `.gitignore`** (`/.gitignore`) — Build artifacts (compiled DLLs, wwwroot output) were swept into this commit. Add `publish/` to `.gitignore`. Non-blocking.

- **N2: Inline `!important` padding** (`MainLayout.razor:L22`) — `Style="padding-top: 80px !important"` works today. If MudBlazor's padding strategy changes in a minor version bump this could conflict. Consider moving to a named CSS class (e.g., `.layout-main-content { padding-top: 80px; }`) for easier future adjustment. Non-blocking; the current implementation is fine for now.

---

## Positive Observations

- ✅ **Route constraint is correct** — `@page "/forms/{Id:int}"` with `[Parameter] public int Id` is textbook Blazor route binding. No issues.
- ✅ **API call path matches controller exactly** — `api/forms/{Id}` → `[Route("api/forms")]` + `[HttpGet("{id:int}")]`. Clean.
- ✅ **404 handling is solid** — `response.StatusCode == NotFound` path sets `_form = null`, shows snackbar error, and renders a "Back to Library" fallback button. Clean and user-friendly. (Build report said "navigate back to /forms" — the actual behavior is better: it shows the error and lets the user decide.)
- ✅ **Null safety is clean** — `FormDetailDto.Fields` initializes to `new()`, all nullable properties use `?.`, `??`, or `HasValue` checks. No `NullReferenceException` risk.
- ✅ **Polling self-stops correctly** — `_pollTimer?.Dispose(); _pollTimer = null;` inside the callback when status is terminal. The `InvokeAsync` dispatch serializes through the UI thread so no race conditions.
- ✅ **Confidence badge thresholds are correct** — `>= 90 → confidence-high`, `>= 70 → confidence-medium`, `< 70 → confidence-low` matches the CSS class semantics.
- ✅ **FormLibrary row links correct** — `MudLink Href="@($"/forms/{context.Id}")"` is already there and correctly targets the new route. No changes needed (confirmed).
- ✅ **AppBar padding math is right** — 64px AppBar height (confirmed in theme) + 16px breathing room = 80px. Correct and defensible.
- ✅ **Loading skeleton, error state, processing state, empty state** — all present and handled properly. Tony covered all the branches.

---

## Acceptance Criteria Verification

| Criterion | Status | How Verified |
|---|---|---|
| Header no longer overlaps content | ✅ | 80px padding-top applied inline, AppBar is 64px — math is correct |
| `/forms/{id}` route binding works | ✅ | `@page "/forms/{Id:int}"` + `[Parameter] public int Id` — correct |
| API call matches controller route | ✅ | `api/forms/{Id}` → `[HttpGet("{id:int}")]` — exact match |
| 404 shows error and back button | ✅ | Verified in code — renders `MudAlert` + `MudButton Href="/forms"` |
| Polling auto-stops when done | ✅ | Self-dispose on terminal status — correct |
| Polling cleans up on navigation | ❌ | **BLOCKED** — `@implements IDisposable` missing; timer not disposed |
| Confidence CSS classes correct | ✅ | Classes exist in `app.css`; thresholds match |
| No null ref on Fields / metadata | ✅ | DTO initializes Fields to `new()`; all nullable access guarded |
| FormLibrary links to FormDetail | ✅ | `MudLink Href="/forms/{context.Id}"` already present, unchanged |

---

## Fix Summary

**One change required, five minutes of work:**

```razor
@page "/forms/{Id:int}"
@using System.Net.Http.Json
@using FortressFormTools.Web.Models
@inject HttpClient Http
@inject NavigationManager Nav
@inject ISnackbar Snackbar
@implements IDisposable          ← ADD THIS LINE
```

Everything else is clean. After that one line is added, this is a **PASS**.

---

_— Hawkeye_
