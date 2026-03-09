# FORMS Header Color — Deeper Investigation

## Previous Attempt
Commit 003f8a7 added `--color-header-bg: #1a2332` to `fip-theme.css`. QA verified the live site still shows `#f8f9fa` (light gray) instead of navy.

## Root Cause
**Stale publish artifacts** — same class of bug as bd48b3e.

The source file `FortressFormTools.Web/wwwroot/css/fip-theme.css` had the correct CSS variables, but **both** `publish/` and `FortressFormTools.Web/publish/` directories contained an old version of `fip-theme.css` missing the Brand Chrome block:
- `--color-header-bg`
- `--color-sidebar-bg`
- `--color-sidebar-hover` / `--color-sidebar-active`
- `--color-gold` / `--color-gold-muted` / `--color-gold-dim`
- `--color-text-inverse`

The MudAppBar in `MainLayout.razor` has an inline style: `background: var(--color-header-bg)`. When the CSS variable is undefined (stale publish CSS), the browser treats the property as "invalid at computed-value time" per the CSS Variables spec → `background` becomes `transparent` → the page background `#f8f9fa` shows through.

**Note:** The MudTheme (`FipTheme.cs`) correctly sets `AppbarBackground = "#1a2332"`, but the explicit inline `Style` attribute on the MudAppBar overrides the theme's class-based styling. When the CSS variable is undefined, the inline style wins with an invalid value, defeating the theme entirely.

## Fix
1. **Rebuilt publish artifacts** — `dotnet publish` to sync all CSS and compiled Razor
2. **Added CSS variable fallback** — Changed `var(--color-header-bg)` → `var(--color-header-bg, #1a2332)` so the header stays navy even if the variable is ever missing again

## Recurring Issue
This is the **second time** stale publish artifacts caused a live bug (first was bd48b3e for the waffle menu). The deploy pipeline should rebuild from source rather than shipping pre-built publish/ artifacts from the repo.

## Build: succeeded ✅
## Commit: f1d88ab
