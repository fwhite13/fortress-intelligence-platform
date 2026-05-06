# CC Task — ADO#2830: User Identity + Role Chip in NEXUS AppBar

## Context

You are working in the NEXUS Blazor Server app at:
`/home/fredw/projects/fip/nexus/src/FortressNexus.Web/`

The task is to add user identity display and a role chip to the AppBar in `MainLayout.razor`.

## Files to Read First

1. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Components/Layout/MainLayout.razor`
2. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/UserContextService.cs`
3. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Components/Layout/MainLayout.razor.css`

## Files to Change

1. `src/FortressNexus.Web/Components/Layout/MainLayout.razor`
2. `src/FortressNexus.Web/Components/Layout/MainLayout.razor.css`

---

## Exact Changes Required

### 1. `MainLayout.razor`

**Add `@inject` directive** at the top of the file (after `@inherits LayoutComponentBase`):
```razor
@inject FortressNexus.Web.Services.UserContextService UserContextService
```

**Modify the AppBar block.** The current AppBar is:
```razor
<MudAppBar Elevation="1">
    <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start" OnClick="ToggleDrawer" />
    <MudText Typo="Typo.h6" Class="ml-3">NEXUS</MudText>
    <MudSpacer />
</MudAppBar>
```

Replace it with:
```razor
<MudAppBar Elevation="1">
    <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start" OnClick="ToggleDrawer" />
    <MudText Typo="Typo.h6" Class="ml-3">NEXUS</MudText>
    <MudSpacer />
    @if (_upn is not null)
    {
        @if (_isAdmin)
        {
            <MudChip T="string" Color="Color.Warning" Size="Size.Small" Class="nexus-header-role-chip">Admin</MudChip>
        }
        else if (_isReviewer)
        {
            <MudChip T="string" Color="Color.Info" Size="Size.Small" Class="nexus-header-role-chip">Reviewer</MudChip>
        }
        <MudText Typo="Typo.body2" Class="nexus-header-upn ml-2 mr-2">@_displayName</MudText>
    }
</MudAppBar>
```

**Modify the `@code` block.** The current `@code` block is:
```csharp
@code {
    private bool _drawerOpen = true;

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;
}
```

Replace it with:
```csharp
@code {
    private bool _drawerOpen = true;
    private string? _upn;
    private string? _displayName;
    private bool _isAdmin;
    private bool _isReviewer;

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;

    protected override async Task OnInitializedAsync()
    {
        _upn = await UserContextService.GetUpnAsync();
        _isAdmin = await UserContextService.IsAdminAsync();
        _isReviewer = !_isAdmin && await UserContextService.IsReviewerAsync();
        // Display only the part before '@' for readability; fall back to full UPN
        _displayName = _upn?.Contains('@') == true ? _upn.Split('@')[0] : _upn;
    }
}
```

### 2. `MainLayout.razor.css`

The file currently contains only:
```css
/* NEXUS MainLayout styles */
```

Append the following CSS (replace the whole file content):
```css
/* NEXUS MainLayout styles */

.nexus-header-role-chip {
    margin-right: 4px;
}

.nexus-header-upn {
    opacity: 0.85;
    max-width: 200px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    color: inherit;
}
```

---

## Constraints

- Do NOT change anything else in the file (drawer, nav links, MudMainContent, etc.)
- Do NOT add inline styles — use the CSS classes only
- Do NOT modify any service files
- The `@inject` must use the fully-qualified namespace `FortressNexus.Web.Services.UserContextService`
- Use scoped CSS in `MainLayout.razor.css` — do not create a new CSS file

## Acceptance Criteria

- AppBar shows user's display name (part before `@`) when logged in
- "Admin" chip (Color.Warning = amber) shown for NexusAdmin role
- "Reviewer" chip (Color.Info = blue) shown for NexusReviewer role
- No chip shown for regular users
- Chip and name appear to the right of the spacer (right side of AppBar)
- No regressions to drawer, nav, or main content

## Output

After making the changes, do a dry-run build check:
```bash
cd /home/fredw/projects/fip/nexus && dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj --no-restore 2>&1 | tail -20
```

Report what you changed and whether the build succeeded.
