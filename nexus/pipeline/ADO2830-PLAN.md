# BUILD Plan — ADO#2830
## NEXUS: Show logged-in user identity and role in header/nav

**WI:** ADO#2830 | Feature #2802 | Epic #2793
**Repo:** `/home/fredw/projects/fip/nexus/`

---

## Context

`MainLayout.razor` currently shows only "NEXUS" in the AppBar with no user identity or role indicator. After ADO#2831 deploys (role claims now injected), users will have real role values available. This WI adds a user identity chip and role badge to the NEXUS header/nav, consistent with other FIP module patterns.

Current MainLayout AppBar:
```razor
<MudAppBar Elevation="1">
    <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start" OnClick="ToggleDrawer" />
    <MudText Typo="Typo.h6" Class="ml-3">NEXUS</MudText>
    <MudSpacer />
</MudAppBar>
```

---

## Required Changes

### `MainLayout.razor`

Add to AppBar after `<MudSpacer />`:
- User display: show UPN/email (truncated if long)
- Role badge: show "Admin", "Reviewer", or nothing for regular users — color-coded

```razor
@inject UserContextService UserContextService

@* In AppBar after MudSpacer *@
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
    <MudText Typo="Typo.body2" Class="nexus-header-upn ml-2">@_upn</MudText>
}
```

In `@code`:
```csharp
private string? _upn;
private bool _isAdmin;
private bool _isReviewer;

protected override async Task OnInitializedAsync()
{
    _upn = await UserContextService.GetUpnAsync();
    _isAdmin = await UserContextService.IsAdminAsync();
    _isReviewer = !_isAdmin && await UserContextService.IsReviewerAsync();
}
```

**UPN display:** If the UPN is long (e.g. `fwhite@fortressaffinitygroup.com`), truncate to first part before `@` for display, or show full UPN — check what other FIP modules do. Use `_upn.Split('@')[0]` or show full — your call, keep it readable.

### CSS

Add to `wwwroot/css/nexus.css` (or wherever NEXUS global styles live — check):
```css
.nexus-header-role-chip {
    margin-right: 4px;
}
.nexus-header-upn {
    opacity: 0.85;
    max-width: 200px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}
```

---

## Acceptance Criteria

- [ ] AppBar shows user's UPN/display name when logged in
- [ ] "Admin" chip (amber/warning) shown for NexusAdmin users
- [ ] "Reviewer" chip (blue/info) shown for NexusReviewer users  
- [ ] No chip shown for regular NexusUser
- [ ] Layout renders correctly on mobile (chips don't overflow)
- [ ] No regressions on existing nav/drawer

---

## Files to change

- `src/FortressNexus.Web/Components/Layout/MainLayout.razor` — main changes
- `wwwroot/css/nexus.css` (or equivalent) — minor CSS additions

---

## CC env vars
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
```
