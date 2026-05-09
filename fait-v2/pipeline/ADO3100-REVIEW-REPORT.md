# Review Report — ADO#3100 — Mobile Responsive Layout

## Verdict: NEEDS-CHANGES

**Reviewer:** Hawkeye (Clint Barton)
**Review Cycle:** 1
**Date:** 2026-05-09
**CC Invocation:** `cat pipeline/ADO3100-review-brief.md | claude --model sonnet --print --dangerously-skip-permissions`
**ADO Comment:** #784195 posted

---

## Checklist Results

| Item | Status |
|---|---|
| `:root` `--mobile-nav-height: 60px` present | ✅ line 134 |
| `:root` `--mobile-breakpoint: 767px` present | ✅ line 135 |
| `@media (max-width: 767px)` block complete | ✅ lines 2414–2565 |
| Sidebar hidden/transformed in media query | ✅ `translateX(-100%) !important` |
| `.mobile-bottom-nav` desktop default `display: none` | ✅ line 2568 |
| `.mobile-only` desktop default `display: none` | ✅ line 2573 |
| `.mobile-bottom-nav` visible in media query (`flex`) | ✅ line 2448 |
| Bottom nav has Chat, Projects, KB, Settings | ✅ lines 157–172 |
| `ToggleDrawer` toggles `body.sidebar-open` | ✅ confirmed |
| **No hardcoded pixel values (CSS variable rule)** | ❌ **9+ violations** |
| Touch targets via CSS variable | ❌ All hardcoded `44px` |

---

## Issues Found

### 🔴 Issue 1 — CSS Variable Rule Violations (Critical)

**Severity: Critical — must fix before merge**

The FAIT v2 CSS variable rule (ALL pixel values must use CSS variables, no hardcoded pixels) is violated across the new media query block. The `44px` touch target value appears **9 times** in 4 selectors with no `--touch-target-size` variable defined in `:root`.

| Location | Value | Violation |
|---|---|---|
| `.mobile-nav-item` | `min-height: 44px` | No `--touch-target-size` var |
| `.mobile-nav-item` | `min-width: 44px` | No `--touch-target-size` var |
| `.chat-input` | `min-height: 44px !important` | Hardcoded |
| `.btn-send` | `min-width/min-height/width/height: 44px` (×4) | Hardcoded |
| `.btn, .mud-button-root…` | `min-height/min-width: 44px` (×2) | Hardcoded |
| `.mobile-bottom-nav` | `border-top: 1px solid …` | `1px` hardcoded |
| `.mobile-bottom-nav` | `box-shadow: 0 -2px 8px …` | `2px`, `8px` hardcoded |
| `.mobile-nav-item .mud-icon-root` | `font-size: 1.25rem` | Raw literal, no variable |

**Required fix:**
1. Add to `:root` in `fortress.css`:
   ```css
   --touch-target-size: 44px;
   --mobile-icon-size: 1.25rem;
   /* Use existing shadow vars if available, or add: */
   --mobile-nav-shadow-offset: 2px;
   --mobile-nav-shadow-blur: 8px;
   ```
2. Replace ALL `44px` occurrences in the mobile block with `var(--touch-target-size)`
3. Replace `font-size: 1.25rem` with `var(--mobile-icon-size)` (or existing icon-size variable if one exists)
4. Replace box-shadow literal pixels with variables or use existing shadow variables
5. The `1px` border can use an existing border variable if one exists in the design system

---

### 🟡 Issue 2 — `eval` in JS Interop — CSP Risk (Important)

**Severity: Important — must fix before merge**

**Location:** `MainLayout.razor` line ~330

```csharp
await JS.InvokeVoidAsync("eval", "document.body.classList.toggle('sidebar-open')");
```

**Problems:**
1. **CSP risk**: ASP.NET Core Blazor apps with `script-src 'self'` (common) block `eval` unless `'unsafe-eval'` is explicitly permitted. If CSP is tightened or already set, this fails silently.
2. **Error swallowed**: This call appears to be inside (or near) the localStorage try/catch block — JS interop failures are silently swallowed with no logging.
3. **Antipattern**: Using `eval` to call `classList.toggle` is avoidable with a named function.

**Required fix:**
1. Add to `wwwroot/js/site.js`:
   ```js
   window.toggleSidebarClass = function () {
       document.body.classList.toggle('sidebar-open');
   };
   ```
2. In `MainLayout.razor`, replace the `eval` call:
   ```csharp
   await JS.InvokeVoidAsync("toggleSidebarClass");
   ```
3. Move this JS interop call outside the localStorage try/catch block so errors surface rather than being swallowed.

---

### 🟡 Issue 3 — Backdrop Overlay Has No Click-to-Close Handler (Important)

**Severity: Important — must fix before merge**

**Location:** `fortress.css` (the `body.sidebar-open::before` rule) + `MainLayout.razor`

The `body.sidebar-open::before` pseudo-element renders a full-screen dark overlay at `z-index: 299`. However, **CSS pseudo-elements cannot have JavaScript event listeners**. There is no Blazor click handler on any backdrop element. Result:

- User opens sidebar on mobile ✓
- Dark overlay appears (implies "tap to dismiss") ✓
- User taps overlay — nothing happens ✗
- Sidebar remains open; only the hamburger button closes it

This is a broken UX expectation — overlays/backdrops universally imply "tap to close." The `_drawerOpen` Blazor state is also now permanently out of sync with visual state.

**Required fix:**
1. Remove the `body.sidebar-open::before` CSS rule
2. In `MainLayout.razor`, add a real DOM element inside `<MudLayout>` (or equivalent container):
   ```html
   @if (_drawerOpen)
   {
       <div class="mobile-sidebar-backdrop" @onclick="ToggleDrawer"></div>
   }
   ```
3. Add CSS for the backdrop (inside `@media (max-width: 767px)` only):
   ```css
   .mobile-sidebar-backdrop {
       position: fixed;
       inset: 0;
       background: rgba(0, 0, 0, 0.5);
       z-index: 299;
   }
   ```

---

### ℹ️ Issue 4 — Desktop Defaults Declared After Media Block (Nitpick)

`.mobile-bottom-nav { display: none; }` (line 2568) and `.mobile-only { display: none; }` (line 2573) are declared after the `@media` block that overrides them. This works because of `!important` on the media query rule, but is fragile — if `!important` were ever removed, the later `display: none` would win on mobile due to cascade order.

**Suggested fix:** Move both desktop-default rules to before the `@media (max-width: 767px)` block.

---

### ℹ️ Issue 5 — `<a>` Not `<NavLink>` in Bottom Nav (Nitpick)

The `.mobile-nav-item.active` CSS rule exists (line 2476) to highlight the current route in the bottom nav, but the nav items use plain `<a>` tags (lines 157–172), not Blazor `<NavLink>` components. The Blazor router never applies the `active` class to plain `<a>` elements, so the active-highlight styling is dead code.

**Suggested fix:** Replace `<a class="mobile-nav-item" href="...">` with `<NavLink class="mobile-nav-item" href="..." Match="NavLinkMatch.All">` for each bottom nav item.

---

## Summary

| # | Issue | Severity | Fix Required Before Merge |
|---|---|---|---|
| 1 | 9+ hardcoded pixel values — CSS variable rule violated | **Critical** | ✅ Yes |
| 2 | `eval` JS interop — CSP risk + silently swallowed | **Important** | ✅ Yes |
| 3 | Backdrop overlay — no click handler, UX broken | **Important** | ✅ Yes |
| 4 | Desktop defaults after media block | Nitpick | Optional |
| 5 | `<a>` not `<NavLink>` — active state never fires | Nitpick | Optional |

**Structural assessment:** The mobile layout skeleton is sound. The media query structure, sidebar transform approach, bottom nav HTML, and variable definitions for `--mobile-nav-height` and `--mobile-breakpoint` are all correct. The three required fixes are well-scoped — Tony can resolve them in a single CC pass.

**Sending back to Tony. Cycle 2 will verify: CSS variable compliance on all 9+ touch-target occurrences, `toggleSidebarClass` function in site.js, and real backdrop DOM element with `@onclick="ToggleDrawer"`.**

---

*"Structure's there. Three things need fixing. Send it back."*
