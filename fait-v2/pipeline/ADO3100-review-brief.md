# Code Review Brief — ADO#3100 — Mobile Responsive Layout

## Working Directory
`/home/fredw/projects/fip/fait-v2`

## What Was Built
Mobile responsive layout for FAIT v2:
- CSS variables `--mobile-nav-height: 60px` and `--mobile-breakpoint: 767px` added to `:root`
- Full `@media (max-width: 767px)` block in `fortress.css`
- `.mobile-bottom-nav` desktop default hidden, visible with `display: flex !important` in media query
- `.mobile-only` desktop default hidden
- `MainLayout.razor` — `ToggleDrawer()` toggles `body.sidebar-open` via JS interop using `eval`
- `<nav class="mobile-bottom-nav">` added with Chat, Projects, KB, Settings items

## Files to Review

### File 1: `src/FortressAI.V2.Web/wwwroot/css/fortress.css`
Read the full file (it's large — focus on lines 130-140 for :root vars, and lines 2400-2580 for the mobile media query and bottom-nav rules).

Tasks:
1. Find `:root` block — verify `--mobile-nav-height: 60px` and `--mobile-breakpoint: 767px` are present
2. Find `@media (max-width: 767px)` block — verify it is complete and contains:
   - Sidebar rules (hidden/transformed by default)
   - `.mobile-bottom-nav { display: flex !important }` 
   - Bottom nav items with touch targets ≥ 44×44px
   - Layout adjustments
3. Find `.mobile-bottom-nav` desktop default — verify `display: none`
4. Find `.mobile-only` desktop default — verify `display: none`
5. **CRITICAL CHECK**: Scan the ENTIRE `@media (max-width: 767px)` block and ALL new CSS rules added. Are there ANY hardcoded pixel values (e.g., `60px`, `44px`, `767px`, etc.) that do NOT use `var(--...)` syntax? The FAIT v2 CSS variable rule is: ALL pixel values must use CSS variables. No hardcoded pixels allowed — even for values that happen to match variables. Flag any violation as Critical.
6. Check touch targets — are they expressed as `var(--touch-target-size)` or similar variable, or hardcoded `44px`?

### File 2: `src/FortressAI.V2.Web/Components/Layout/MainLayout.razor`
Read the full file (focus on the ToggleDrawer method and the mobile-bottom-nav HTML block).

Tasks:
1. Find `ToggleDrawer` method — read the full implementation
2. Check the JS interop call — is it `JS.InvokeVoidAsync("eval", "document.body.classList.toggle('sidebar-open')")`?
3. Find `<nav class="mobile-bottom-nav">` — verify it has Chat, Projects, KB, Settings items
4. Check each bottom nav item — do they have proper href/NavLink? Are they functional?
5. Check for any click-to-close handler on the backdrop overlay — is there one?
6. Look at `_drawerOpen` state — how is it used vs. the CSS body class?

## Specific Issues to Assess

### Issue 1: `eval` usage (Tony flagged)
The ToggleDrawer call: `JS.InvokeVoidAsync("eval", "document.body.classList.toggle('sidebar-open')")`

Assess:
- **CSP risk**: Content Security Policy in Blazor apps typically blocks `eval`. Is this a real CSP violation risk?
- **Code quality**: Using `eval` for DOM manipulation is an antipattern — a proper function in `site.js` would be cleaner
- **Severity**: Critical (blocks deploy), Important (must fix but not a blocker), or Nitpick?
- **Recommended fix**: If it needs fixing, specify exact fix — e.g., "Add `toggleSidebarClass()` function to `wwwroot/js/site.js` and call `JS.InvokeVoidAsync("toggleSidebarClass")`"

### Issue 2: Backdrop state drift (Tony flagged)
`body.sidebar-open::before` overlay is CSS-only pseudo-element. Tapping it closes sidebar visually (CSS removes `body.sidebar-open` class?) — wait, actually CSS pseudo-elements can't capture click events directly.

Re-examine: How does the overlay/backdrop work exactly?
- Is `body.sidebar-open::before` a click-catchable element? (pseudo-elements can't have JS event handlers)
- If the user taps outside the sidebar on mobile, what happens to `_drawerOpen` Blazor state?
- What is the actual UX: does the sidebar stay open when you tap the backdrop, or does something else happen?
- Severity: Critical (sidebar can't be closed on mobile = UX broken), Important (state inconsistency), Nitpick?

## Review Checklist
- [ ] `:root` variables present and correct
- [ ] `@media (max-width: 767px)` block complete
- [ ] `.mobile-bottom-nav` desktop hidden
- [ ] `.mobile-only` desktop hidden  
- [ ] Bottom nav visible in media query with flex
- [ ] **NO hardcoded pixel values** (CSS variable rule — check every single new value)
- [ ] Touch targets compliant (≥ 44×44px — but via variables)
- [ ] `ToggleDrawer` toggles `body.sidebar-open`
- [ ] Bottom nav has Chat, Projects, KB, Settings
- [ ] `eval` usage — assessed and severity determined
- [ ] Backdrop state drift — assessed and severity determined

## Output Required
After reading both files via your tools, provide:
1. **Verdict**: PASS / NEEDS-CHANGES / FAIL
2. **Issues list** with severity (Critical / Important / Nitpick) for each
3. **eval assessment**: severity + exact fix if needed
4. **Backdrop drift assessment**: severity + exact fix if needed
5. **CSS variable compliance**: any hardcoded pixel values found?
6. **Touch target compliance**: how are touch targets expressed?
