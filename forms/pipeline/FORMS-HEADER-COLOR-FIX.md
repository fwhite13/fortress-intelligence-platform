# FORMS Header Color Fix

## Issue
Header rendering white instead of Fortress Navy (#1a2332)

## Root Cause
The CSS variable `--color-header-bg` was **never defined** in any CSS file loaded by `App.razor`.

- `App.razor` loads: `css/fortress.css`, `css/app.css`, `css/fip-theme.css`, `css/fip-nav.css`
- None of these defined `--color-header-bg`
- There was a `wwwroot/app.css` (root level) that defined it as `#1E293B`, but this file is NOT referenced in `App.razor`
- The MudAppBar uses `Style="background: var(--color-header-bg)"` — with undefined var, CSS treats the property as "invalid at computed value time" → `unset` → `transparent`
- Result: white/transparent header despite MudBlazor theme having `AppbarBackground = "#1a2332"` (inline Style overrides theme)

## Fix
Added `--color-header-bg: #1a2332` and related FIP design tokens (`--color-sidebar-bg`, `--color-gold`, `--color-text-inverse`, etc.) to `css/fip-theme.css`, which is the canonical FIP tokens file loaded by App.razor.

### Files Changed
- `FortressFormTools.Web/wwwroot/css/fip-theme.css` — added missing FIP Brand Chrome tokens

## Build: succeeded ✅
## Commit: 003f8a7

## Additional Fix: Icon Spacing
- Hamburger and avatar were too inset (36px total: 20px AppBar padding + 16px inner div padding)
- Reduced AppBar padding to 8px, removed inner div padding-left/padding-right
- Now matches tighter edge spacing
- Commit: 507120a
