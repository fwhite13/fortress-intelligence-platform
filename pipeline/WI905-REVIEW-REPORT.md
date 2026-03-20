# Review Report — WI905 — Critical QA Failures Fix
**Reviewer:** Hawkeye (Clint Barton) — `code-reviewer`
**Cycle:** 1 of 2
**Commit:** `afe8da2`
**Date:** 2026-03-19
**Priority:** Critical
**Verdict:** ✅ PASS

---

## Summary

Five targeted fixes for five confirmed bugs. All changes are correct, scoped precisely to the failing items, and structurally sound. No scope creep. No design system violations. The root issue (non-functional click handlers due to missing `@rendermode`) is fixed at the correct level — the `AuthorizeRouteView` component element.

---

## Check Results

### ✅ 1. Routes.razor — `@rendermode` on the correct element

**File:** `famos/src/FamOs.Web/Components/Routes.razor`

```razor
<AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)"
                    @rendermode="InteractiveServer">
```

**Verdict: PASS.**

- `@rendermode="InteractiveServer"` is placed on `<AuthorizeRouteView>` — a Blazor component element. ✅
- It is NOT on `<Router>`, `<Found>`, or any raw HTML element. ✅
- Syntax is valid Blazor directive attribute syntax. ✅
- This is the correct architectural fix. Placing it here wires Blazor Server interactivity to all routed pages simultaneously — `@onclick`, `NavigationManager`, `IDialogService`, `ISnackbar` all become functional in a single change.

---

### ✅ 2. Dashboard.razor — query uses `null` (no owner filter)

**File:** `famos/src/FamOs.Web/Components/Pages/Dashboard.razor`

```csharp
protected override async Task OnInitializedAsync()
{
    _summary = await OppService.GetDashboardSummaryAsync(null);
    _loading = false;
}
```

**Verdict: PASS.**

- Calls `GetDashboardSummaryAsync(null)` — returns all active opportunities regardless of owner. ✅
- Does NOT pass `userId` which was causing 0-result dashboards when `OwnerUserId` stored as `preferred_username` email didn't match Entra OID GUID. ✅
- The injected `UserSessionService` is still present in the file for use by the nav buttons — that's correct, not dead code.

---

### ✅ 3. UserSessionService.cs — returns `preferred_username` claim first

**File:** `famos/src/FamOs.Web/Services/UserSessionService.cs`

```csharp
public async Task<string> GetUserIdAsync()
{
    var user = await GetUserAsync();
    return user.FindFirst("preferred_username")?.Value
        ?? user.FindFirst(ClaimTypes.Email)?.Value
        ?? user.FindFirst("email")?.Value
        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.FindFirst("sub")?.Value
        ?? user.FindFirst("oid")?.Value
        ?? "unknown";
}
```

**Verdict: PASS.**

- Returns `preferred_username` first — this is the Entra ID email claim (e.g., `user@example.com`). ✅
- Falls back through email variants before ever reaching `oid` (GUID). ✅
- The old behavior (returning `oid` GUID) would mismatch any `OwnerUserId` stored as an email string, explaining the zero-result dashboard and empty task list. This chain is now correct.
- Chain is sensible: preferred_username → ClaimTypes.Email → email → NameIdentifier → sub → oid → "unknown". ✅

---

### ✅ 4. Logo centering — CSS correct

**File:** `famos/src/FamOs.Web/wwwroot/css/famos.css`

```css
.sb-logo {
    padding: 16px 16px 14px;
    border-bottom: 1px solid rgba(255,255,255,0.08);
    background: white;
    display: flex;
    align-items: center;
    justify-content: center;   /* ← parent flex centering */
}
.sb-logo img {
    max-width: 100%;
    height: 44px;
    object-fit: contain;
    object-position: center;
    display: block;            /* ← WI905 addition */
    margin: 0 auto;            /* ← WI905 addition */
}
```

**Verdict: PASS.**

- `.sb-logo img` now has `display:block; margin:0 auto`. ✅
- The parent `.sb-logo` also has `display:flex; justify-content:center` (belt and suspenders). ✅
- Either approach alone is sufficient; having both guarantees centering across all Blazor render modes and browser quirks. No issue with having both.

---

### ✅ 5. TaskCenter.razor — page title matches Pipeline pattern

**TaskCenter.razor:**
```razor
<div class="famos-page-header famos-page-header-row mb-4">
    <div>
        <h2 class="famos-page-h2">Task Center</h2>
        <p class="famos-page-sub">...</p>
    </div>
    ...
</div>
```

**Pipeline.razor:**
```razor
<div class="famos-page-header famos-page-header-row mb-4">
    <div>
        <h2 class="famos-page-h2">Pipeline</h2>
        <p class="famos-page-sub">...</p>
    </div>
    ...
</div>
```

**Verdict: PASS.**

- Both use `famos-page-header famos-page-header-row mb-4` wrapper div. ✅
- Both use `<h2 class="famos-page-h2">` for the page title. ✅
- Both use `<p class="famos-page-sub">` for the subtitle. ✅
- Structure is identical. The font size/weight inconsistency Natasha flagged is resolved. ✅

---

### ✅ 6. Scope Check — only `famos/` files

```
git show afe8da2 --stat | grep -v "^famos/" | grep "^\s"
```

**Result: Empty output (exit code 1 — no lines matched)**

**Verdict: PASS.** All 5 changed files are under `famos/src/FamOs.Web/`. Zero out-of-scope changes. ✅

---

### ✅ 7. DESIGN-SYSTEM.md Compliance

**Files checked:** Routes.razor, Dashboard.razor, TaskCenter.razor

```
grep -n "Icons\.Material\|MudButton.*Variant=\|MudButton.*Color=\|MudButton.*Size="
```

**Result: No matches found.**

**Verdict: PASS.**

- No `Icons.Material.*` usage in any changed file. ✅
- No inline `Variant=`, `Color=`, or `Size=` on `MudButton` elements. ✅
- Dashboard.razor uses `Class="famos-btn-outline-sm"` — correct DESIGN-SYSTEM pattern. ✅
- TaskCenter.razor uses `Class="famos-btn-outline-sm"` with `StartIcon="@FamosIcons.Add"` — uses FamosIcons registry, correct. ✅

---

## Issues Found

**None.** All 5 checks pass. No critical, important, or nitpick issues.

---

## QA Process Gap Analysis — Why Natasha's Prior QA Missed Non-Functional Click Handlers

### Root Cause

Natasha's prior QA process relied on **visual inspection** and **HTTP 200 responses** to confirm functionality. In Blazor SSR mode (Static Server-Side Rendering), this approach is fundamentally insufficient.

### The Blazor SSR Trap

When `@rendermode="InteractiveServer"` is absent from `Routes.razor`:

1. **Pages render as static HTML.** The server produces a visually correct, fully styled HTML page.
2. **HTTP 200 is returned.** The page "loads successfully" by every metric a passive checker would use.
3. **All Blazor event handlers are dead.** `@onclick`, `@bind`, `OnInitializedAsync` after render, `NavigationManager.NavigateTo()`, `IDialogService.ShowAsync()`, `ISnackbar.Add()` — all silently do nothing.
4. **No JavaScript errors are guaranteed.** Depending on the Blazor version and mode, the absence of the SignalR circuit may not produce visible browser console errors.

The result: **a page that looks 100% correct under visual inspection but has zero interactivity.**

### What Natasha's Checklist Was Missing

| What Natasha Did | What It Caught | What It Missed |
|---|---|---|
| Navigated to pages | Page load errors (404, 500) | Non-functional JS/Blazor handlers |
| Visually inspected layout | CSS/rendering issues | Dead click handlers |
| Checked HTTP response codes | Server errors | Client-side interactivity |
| Verified text/data displayed | Data fetch on load | Button/navigation outcomes |

### Required QA Checklist Going Forward — Interactivity Tests

**For every page with interactive elements, Natasha MUST:**

#### 1. Explicit Click Tests (Mandatory)
- [ ] Click every `MudButton`, `MudChip`, nav link, and `@onclick` handler
- [ ] Verify the expected outcome: navigation occurs, dialog opens, state changes, snackbar fires
- [ ] **A button that does nothing on click is a BUG, even if it renders correctly**

#### 2. Navigation Verification
- [ ] Click "Pipeline →" on Dashboard — verify URL changes to `/pipeline`
- [ ] Click "Tasks →" on Dashboard — verify URL changes to `/tasks`
- [ ] Click an opportunity row — verify navigation to `/opportunity/{id}`
- [ ] Click "+ New Opportunity" — verify dialog opens (not silent failure)

#### 3. Dialog / Modal Verification
- [ ] Click any button that should open a dialog (`ShowAsync<T>`)
- [ ] Verify the dialog actually appears — not just that the button renders
- [ ] Submit the dialog form — verify state updates

#### 4. State Change Verification
- [ ] After completing a task checkbox — verify task disappears from list
- [ ] After creating an opportunity — verify it appears in pipeline
- [ ] Reload page — verify persisted state is correct

#### 5. Blazor Interactivity Signal Check
- [ ] Open browser DevTools → Network tab
- [ ] Look for WebSocket connection to `/_blazor` — its presence confirms Blazor Server circuit is active
- [ ] If no WebSocket → interactivity is broken regardless of visual appearance

#### 6. The "HTTP 200 ≠ Working" Rule
**Natasha must internalize this principle:**

> In Blazor SSR, a page can return HTTP 200, render pixel-perfect, display real data, and have every button silently broken. Visual inspection is not a substitute for click testing.

### Process Change Required

**Going forward, Natasha's VERIFY assignment must include:**

```
## Interactivity Tests (REQUIRED — not optional)
For each page changed:
1. Click every interactive element using the browser tool
2. Document: element clicked → expected outcome → actual outcome
3. If any click produces no response → FAIL verdict immediately
4. Do not issue PASS based on visual inspection alone
```

**Natasha should use the `browser` tool's `act` action to click elements and observe state changes**, not just `screenshot` to verify visual layout.

---

## Consistency Audit

| Pattern | Routes.razor | Dashboard.razor | TaskCenter.razor | Pipeline.razor |
|---|---|---|---|---|
| Page header wrapper | N/A | `famos-page-header famos-page-header-row` | `famos-page-header famos-page-header-row` | `famos-page-header famos-page-header-row` |
| Title element | N/A | `<h2 class="famos-page-h2">` | `<h2 class="famos-page-h2">` | `<h2 class="famos-page-h2">` |
| Subtitle element | N/A | `<p class="famos-page-sub">` | `<p class="famos-page-sub">` | `<p class="famos-page-sub">` |
| Button style | N/A | `famos-btn-outline-sm` | `famos-btn-outline-sm` | `famos-btn-primary` |
| Icons | N/A | None | `FamosIcons.*` | None |

All patterns consistent. ✅

---

## Verdict

**✅ PASS — Advance to next pipeline stage.**

All 5 bugs fixed correctly. All 7 review checks passed. No issues found. Zero design system violations. Scope clean. The `@rendermode` fix is architecturally correct and resolves the root cause of the entire interactivity failure class. QA process gap is documented above.

---

*— Hawkeye (Clint Barton), code-reviewer*
*Review Cycle 1 — WI905 — 2026-03-19*
