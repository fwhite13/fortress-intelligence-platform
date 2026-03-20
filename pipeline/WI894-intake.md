# WI#894 — FAM OS: Layout Fix + White Topbar + Dashboard Text Fixes

**Priority:** 2 (High)
**Tags:** famos; branding; layout; hotfix

## Problem
After WI#893 removed the FIP navy header bar, three issues remain:
1. **Phantom top spacing** — page content is shifted down as if a header is still there (MudBlazor's `MudMainContent` reserves space for `MudAppBar` by default via `--mud-appbar-height` CSS var or padding-top on the main content area)
2. **Missing white topbar** — the reference portal (IAAPA) has a white top bar with portal name/breadcrumb + user info on left, search on right. We removed the FIP bar but didn't add the correct replacement.
3. **Wrong text** — "FAM OS Dashboard" heading should be "Titan Insurance Group" (the org name from AffinityConfig.DisplayName); Pipeline View button on dashboard navigates to `/pipeline` but does nothing (likely a routing or nav issue)

## Reference
IAAPA portal topbar structure (from IAAPA_Portal_v2_restyled.html):
```css
.topbar {
  background: white;
  border-bottom: 1px solid var(--border);
  padding: 0 26px;
  height: 54px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-shrink: 0;
}
.topbar-crumb { font-size: 13px; color: var(--muted); }
.topbar-crumb strong { color: var(--text); font-weight: 700; }
.topbar-right { display: flex; align-items: center; gap: 10px; }
```

HTML:
```html
<div class="topbar">
  <span class="topbar-crumb">
    IAAPA Insurance › <strong>Dashboard</strong>
    <span class="topbar-role-chip">Scott · Program Director</span>
  </span>
  <div class="topbar-right">
    <!-- search box -->
  </div>
</div>
```

## Changes Required

### 1. MainLayout.razor — Add white topbar, fix phantom spacing

Replace `<MudMainContent>` section with a layout that includes a white topbar:

```razor
<MudMainContent Style="padding-top: 0 !important;">
    <div class="famos-topbar">
        <span class="famos-topbar-crumb">
            @_affinity.DisplayName
            <span class="famos-topbar-sep">›</span>
            <strong>@_pageTitle</strong>
        </span>
        <div class="famos-topbar-right">
            <div class="famos-topbar-search">
                <span class="famos-topbar-search-icon">🔍</span>
                <input type="text" placeholder="Search opportunities..." />
            </div>
            <div class="famos-topbar-user">
                <div class="famos-topbar-avatar">@_userInitial</div>
                <span class="famos-topbar-username">@_userName</span>
            </div>
        </div>
    </div>
    <div style="padding: 24px 28px;">
        @Body
    </div>
</MudMainContent>
```

The `padding-top: 0 !important` on `MudMainContent` overrides MudBlazor's default appbar offset. Alternatively, add to CSS:
```css
.mud-main-content { padding-top: 0 !important; }
```

**Note:** `_pageTitle` should be set via a cascading parameter or event from child pages. For Phase 1, default to "Dashboard" and let pages update it. Or simply show just the `DisplayName` breadcrumb without the dynamic page name — that's fine for now.

### 2. CSS additions (famos.css)

```css
/* White topbar */
.famos-topbar {
    background: white;
    border-bottom: 1px solid #e5e7eb;
    padding: 0 26px;
    height: 54px;
    display: flex;
    align-items: center;
    justify-content: space-between;
    flex-shrink: 0;
    position: sticky;
    top: 0;
    z-index: 100;
}
.famos-topbar-crumb {
    font-size: 13px;
    color: #6b7280;
    display: flex;
    align-items: center;
    gap: 6px;
}
.famos-topbar-crumb strong {
    color: #111827;
    font-weight: 700;
}
.famos-topbar-sep {
    color: #d1d5db;
}
.famos-topbar-right {
    display: flex;
    align-items: center;
    gap: 12px;
}
.famos-topbar-search {
    position: relative;
}
.famos-topbar-search input {
    padding: 7px 12px 7px 30px;
    border-radius: 7px;
    border: 1.5px solid #e5e7eb;
    background: #f9fafb;
    font-size: 12.5px;
    font-family: inherit;
    outline: none;
    width: 240px;
    transition: all 0.15s;
}
.famos-topbar-search input:focus {
    border-color: #002050;
    background: white;
}
.famos-topbar-search-icon {
    position: absolute;
    left: 9px;
    top: 50%;
    transform: translateY(-50%);
    font-size: 13px;
    color: #9ca3af;
    pointer-events: none;
}
.famos-topbar-user {
    display: flex;
    align-items: center;
    gap: 8px;
}
.famos-topbar-avatar {
    width: 30px;
    height: 30px;
    border-radius: 9px;
    background: linear-gradient(135deg, #0090d0, #10b0f0);
    color: white;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 11px;
    font-weight: 800;
    flex-shrink: 0;
}
.famos-topbar-username {
    font-size: 12.5px;
    font-weight: 600;
    color: #374151;
}

/* Remove MudBlazor appbar offset from main content */
.mud-main-content {
    padding-top: 0 !important;
}
```

### 3. Dashboard.razor — Fix heading and Pipeline View button

**Heading:** Change "FAM OS Dashboard" to use `AffinityConfig.DisplayName`:
```razor
@inject Microsoft.Extensions.Options.IOptions<FamOs.Web.Theme.AffinityConfig> AffinityOptions

<h2 class="famos-page-h2">@AffinityOptions.Value.DisplayName</h2>
```
This will display "Titan Insurance Group" for TIG.

**Pipeline View button:** The button calls `GoToPipeline()` which does `Nav.NavigateTo("/pipeline")` — this should work. Check if `/pipeline` route exists in `Pipeline.razor`. If it does, the issue may be that `NavigateTo` needs `forceLoad: false` explicitly, or there's a route mismatch. Fix:
```csharp
private void GoToPipeline() => Nav.NavigateTo("/pipeline", forceLoad: false);
```
If `/pipeline` route doesn't exist or uses a different path, correct the navigation target.

### 4. Remove duplicate user avatar from sidebar bottom

The sidebar currently has a user avatar/name section at the bottom. Since we're adding the user info to the topbar, the sidebar user section can be simplified or removed to avoid duplication. Keep it minimal (just the avatar initial as a status indicator, or remove entirely).

## Additional Issues (added 2026-03-19 15:40 EDT)

### 5. TIG logo centering in sidebar
The `.sb-logo img` currently uses `object-position: left`. Center it:
```css
.sb-logo {
    padding: 16px;
    border-bottom: 1px solid rgba(255,255,255,0.08);
    background: white;
    display: flex;
    align-items: center;
    justify-content: center;
}
.sb-logo img {
    max-width: calc(100% - 24px);
    height: 44px;
    object-fit: contain;
    object-position: center;
}
```

### 6. Button style consistency
Three buttons with different styles across the app — normalize them all to the same outlined secondary style:
- Dashboard: "Pipeline View" (`famos-btn-outline-sm`)
- Pipeline: "New Opportunity" (check current class)
- Task Center: "Add Task" (check current class)

All three should use `famos-btn-outline-sm` or equivalent — same border, same font size, same padding.

### 7. Topbar search icon — replace emoji with monochrome SVG
Replace the `🔍` emoji in `.famos-topbar-search-icon` with a proper monochrome SVG search icon:
```razor
<span class="famos-topbar-search-icon">
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
        <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
    </svg>
</span>
```
Color should be `#9ca3af` (muted gray) matching the design system.

### 8. Task Center filter icon — use filter/funnel icon, not spyglass
The Task Center "Filter by opportunity" uses a spyglass/search icon. Replace with a funnel/filter icon to distinguish search (find records) from filter (narrow existing list):
```razor
<svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
    <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/>
</svg>
```

## Acceptance Criteria
- [ ] No phantom top spacing — content starts immediately below the new white topbar
- [ ] White topbar present: `DisplayName › [page]` breadcrumb on left, search input on right, user avatar + name
- [ ] "FAM OS Dashboard" / "Truckers Insurance Group" text → AffinityConfig.DisplayName ("Titan Insurance Group")
- [ ] Pipeline View button on dashboard navigates to pipeline page correctly
- [ ] TIG logo centered in white sidebar area (not left-aligned)
- [ ] Pipeline View / New Opportunity / Add Task all use same button style
- [ ] Topbar search icon is monochrome SVG (not emoji)
- [ ] Task Center filter uses funnel icon, not spyglass
- [ ] No visual regressions on Pipeline, Opportunity Workspace, or other pages

## Build
- Monorepo: `~/projects/fip/`
- AffinityConfig already in appsettings.json from WI#893
- No DB changes
