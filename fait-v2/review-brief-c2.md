# C2 Review Brief — ADO#3100 Mobile Responsive Layout

Commit to verify: `3524bcc7`

I need you to verify ALL 5 fixes from the C1 review cycle. For each fix, read the actual file contents and confirm what is or is not present. Be precise and quote exact lines where relevant.

## Files to Check
- `wwwroot/css/fortress.css`
- `wwwroot/js/app.js`
- `Components/Layout/MainLayout.razor`
- `Components/Layout/NavMenu.razor` (if relevant for bottom nav)
- Any other .razor files containing the bottom nav component

---

## Fix 1 — CRITICAL: CSS variable compliance (`fortress.css`)

Check the `:root` block for these variables:
- `--touch-target-size`
- `--mobile-icon-size`
- `--mobile-nav-border`
- `--mobile-nav-shadow`

Then check the `@media (max-width: 767px)` block:
- All `44px` occurrences inside the mobile block should use `var(--touch-target-size)` not raw `44px`
- Icon font-size should use `var(--mobile-icon-size)` not raw `1.25rem`
- box-shadow should use `var(--mobile-nav-shadow)`
- border-top should use `var(--mobile-nav-border)`

Grep for any remaining raw `44px` or `1.25rem` inside the mobile media block.

## Fix 2 — IMPORTANT: `eval` replaced with named function

Check `app.js`:
- Is there a `window.toggleSidebarClass` function defined?
- No `eval(` present in the file

Check `MainLayout.razor`:
- `JS.InvokeVoidAsync("toggleSidebarClass")` present
- Is it outside any try/catch block?

## Fix 3 — IMPORTANT: Real backdrop element with click handler

Check `fortress.css`:
- `body.sidebar-open::before` pseudo-element rule — is it REMOVED?

Check `MainLayout.razor`:
- Is there a conditional `@if (_drawerOpen)` or `@if (drawerOpen)` block rendering a `<div class="mobile-sidebar-backdrop"` with `@onclick`?

Check `fortress.css` for `.mobile-sidebar-backdrop` CSS:
- Should have `position: fixed`, `inset: 0` (or equivalent top/right/bottom/left: 0), `background: rgba(0,0,0,0.5)`, `z-index: 299` (or similar high value)

## Fix 4 — NITPICK: NavLink in bottom nav

Find the bottom nav component (check MainLayout.razor and any NavMenu/BottomNav razor files):
- All 4 bottom-nav items should use `<NavLink>` with `Match="NavLinkMatch.All"` 
- No plain `<a>` tags for bottom-nav items

## Fix 5 — NITPICK: Desktop defaults before media block

Check `fortress.css`:
- `.mobile-bottom-nav { display: none }` appears BEFORE the `@media (max-width: 767px)` block
- `.mobile-only { display: none }` appears BEFORE the `@media (max-width: 767px)` block

---

## Output Required

For each fix, state:
- ✅ VERIFIED or ❌ FAILED or ⚠️ PARTIAL
- Quote the exact relevant lines found
- If failed, state exactly what is wrong

End with overall verdict: PASS (all verified) or NEEDS-CHANGES (any failed/partial).
