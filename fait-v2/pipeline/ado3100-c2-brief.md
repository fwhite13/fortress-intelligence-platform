# ADO#3100 Cycle 2 Fix Brief

Working directory: /home/fredw/projects/fip/fait-v2
Target files:
- src/FortressAI.V2.Web/wwwroot/css/fortress.css
- src/FortressAI.V2.Web/Components/Layout/MainLayout.razor
- src/FortressAI.V2.Web/wwwroot/js/app.js

## Fix 1 — CRITICAL: CSS Variable compliance in fortress.css

### 1a. Add new variables to :root block
In fortress.css, find the `:root` block. After `--mobile-breakpoint: 767px;` and before the closing `}`, insert these new CSS variables:
```
  --touch-target-size: 44px;
  --mobile-icon-size: 1.25rem;
  --mobile-nav-border: 1px;
  --mobile-nav-shadow: 0 -2px 8px rgba(0, 0, 0, 0.08);
```

### 1b. Replace raw pixel/rem values in the @media (max-width: 767px) block
In the `@media (max-width: 767px)` block, make these replacements:

In `.mobile-nav-item`:
- `min-height: 44px;` → `min-height: var(--touch-target-size);`
- `min-width: 44px;` → `min-width: var(--touch-target-size);`

In `.mobile-nav-item .mud-icon-root`:
- `font-size: 1.25rem;` → `font-size: var(--mobile-icon-size);`

In `.mobile-bottom-nav`:
- `border-top: 1px solid rgba(255, 255, 255, 0.1);` → `border-top: var(--mobile-nav-border) solid rgba(255, 255, 255, 0.1);`
- `box-shadow: 0 -2px 8px rgba(0, 0, 0, 0.2);` → `box-shadow: var(--mobile-nav-shadow);`

In `.chat-input`:
- `min-height: 44px !important;` → `min-height: var(--touch-target-size) !important;`

In `.btn-send`:
- `min-width: 44px !important;` → `min-width: var(--touch-target-size) !important;`
- `min-height: 44px !important;` → `min-height: var(--touch-target-size) !important;`
- `width: 44px !important;` → `width: var(--touch-target-size) !important;`
- `height: 44px !important;` → `height: var(--touch-target-size) !important;`

In `.btn, .mud-button-root, .btn-icon, .hamburger-btn`:
- `min-height: 44px !important;` → `min-height: var(--touch-target-size) !important;`
- `min-width: 44px !important;` → `min-width: var(--touch-target-size) !important;`

After all replacements, verify: `grep -n "44px" src/FortressAI.V2.Web/wwwroot/css/fortress.css` should return 0 results inside the @media block.

## Fix 2 — IMPORTANT: Replace eval with named JS function

### 2a. Add to app.js (append at end of file)
```js
window.toggleSidebarClass = function () {
    document.body.classList.toggle('sidebar-open');
};
```

### 2b. Update MainLayout.razor ToggleDrawer method
Find the ToggleDrawer private method. Currently it looks like:
```csharp
    private async Task ToggleDrawer()
    {
        _drawerOpen = !_drawerOpen;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", "fait-v2-drawer-open", _drawerOpen.ToString().ToLower());
            // On mobile: toggle sidebar-open class on body for CSS-driven slide-in
            await JS.InvokeVoidAsync("eval", "document.body.classList.toggle('sidebar-open')");
        }
        catch { /* localStorage not available */ }
    }
```

Replace it with:
```csharp
    private async Task ToggleDrawer()
    {
        _drawerOpen = !_drawerOpen;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", "fait-v2-drawer-open", _drawerOpen.ToString().ToLower());
        }
        catch { /* localStorage not available */ }
        // On mobile: toggle sidebar-open class on body for CSS-driven slide-in
        await JS.InvokeVoidAsync("toggleSidebarClass");
    }
```

Note: the toggleSidebarClass call is OUTSIDE the try/catch so errors surface.

## Fix 3 — IMPORTANT: Replace CSS backdrop pseudo-element with real DOM element + click handler

### 3a. Remove the CSS pseudo-element rule from fortress.css
In fortress.css, find and remove (delete entirely) this rule:
```css
  /* Sidebar overlay backdrop when open */
  body.sidebar-open::before {
    content: '';
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.45);
    z-index: 299;
  }
```

### 3b. Add backdrop CSS rule to the @media block (keep mobile-only)
In the @media (max-width: 767px) block, add this rule (place it after the `body.sidebar-open .mud-drawer` rule or near the other sidebar rules):
```css
  .mobile-sidebar-backdrop {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.5);
    z-index: 299;
  }
```

### 3c. Add backdrop element in MainLayout.razor
In MainLayout.razor, find `<MudLayout>` opening tag. Just after it (as the first child element), add:
```html
@if (_drawerOpen)
{
    <div class="mobile-sidebar-backdrop" @onclick="ToggleDrawer"></div>
}
```

## Fix 4 — NITPICK: Replace <a> tags with <NavLink> in bottom nav

In MainLayout.razor, find the bottom nav section:
```html
<nav class="mobile-bottom-nav">
    <a href="/" class="mobile-nav-item">
        <MudIcon Icon="@Icons.Material.Filled.Chat" />
        <span>Chat</span>
    </a>
    <a href="/projects" class="mobile-nav-item">
        <MudIcon Icon="@Icons.Material.Filled.Folder" />
        <span>Projects</span>
    </a>
    <a href="/knowledge-base" class="mobile-nav-item">
        <MudIcon Icon="@Icons.Material.Filled.MenuBook" />
        <span>KB</span>
    </a>
    <a href="/settings" class="mobile-nav-item">
        <MudIcon Icon="@Icons.Material.Filled.Settings" />
        <span>Settings</span>
    </a>
</nav>
```

Replace with:
```html
<nav class="mobile-bottom-nav">
    <NavLink class="mobile-nav-item" href="/" Match="NavLinkMatch.All">
        <MudIcon Icon="@Icons.Material.Filled.Chat" />
        <span>Chat</span>
    </NavLink>
    <NavLink class="mobile-nav-item" href="/projects" Match="NavLinkMatch.All">
        <MudIcon Icon="@Icons.Material.Filled.Folder" />
        <span>Projects</span>
    </NavLink>
    <NavLink class="mobile-nav-item" href="/knowledge-base" Match="NavLinkMatch.All">
        <MudIcon Icon="@Icons.Material.Filled.MenuBook" />
        <span>KB</span>
    </NavLink>
    <NavLink class="mobile-nav-item" href="/settings" Match="NavLinkMatch.All">
        <MudIcon Icon="@Icons.Material.Filled.Settings" />
        <span>Settings</span>
    </NavLink>
</nav>
```

## Fix 5 — NITPICK: Move desktop-default rules before media block

In fortress.css, at the bottom of the file, find:
```css
/* Bottom nav hidden on desktop by default */
.mobile-bottom-nav {
  display: none;
}

/* mobile-only elements hidden on desktop */
.mobile-only {
  display: none;
}
```

These rules currently appear AFTER the @media block. Move them to BEFORE the `@media (max-width: 767px)` block. Delete them from their current location and insert them immediately before the `@media` block.

## After all changes

1. Run: `dotnet build src/FortressAI.V2.Web/FortressAI.V2.Web.csproj`
2. Confirm: 0 errors
3. Run: `git -C /home/fredw/projects/fip/fait-v2 add src/FortressAI.V2.Web/wwwroot/css/fortress.css src/FortressAI.V2.Web/Components/Layout/MainLayout.razor src/FortressAI.V2.Web/wwwroot/js/app.js`
4. Run: `git -C /home/fredw/projects/fip/fait-v2 commit -m "fix(fait#3100): CSS variable compliance, replace eval with named JS fn, real backdrop element, NavLink for active state"`
5. Report the commit hash
