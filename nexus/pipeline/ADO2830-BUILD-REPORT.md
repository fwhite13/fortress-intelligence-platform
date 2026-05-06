# Build Report — ADO#2830

## What was built
Added user identity (display name) and role chip to the NEXUS AppBar. NexusAdmin users see an amber "Admin" chip; NexusReviewer users see a blue "Reviewer" chip; regular users see no chip. The username part before `@` is shown for readability.

## Files changed
- `src/FortressNexus.Web/Components/Layout/MainLayout.razor` — added `@inject UserContextService`, added role chip + UPN display in AppBar after `<MudSpacer />`, added `OnInitializedAsync` with `_upn`, `_displayName`, `_isAdmin`, `_isReviewer` fields
- `src/FortressNexus.Web/Components/Layout/MainLayout.razor.css` — added `.nexus-header-role-chip` and `.nexus-header-upn` scoped CSS classes

## Commit
`9cad377` — `feat(ADO#2830): show user identity and role chip in NEXUS AppBar`

## Parallelization used
No — single file pair, sequential.

## CC sessions run
1 (CC Sonnet, pipe mode)

## Build result
✅ **SUCCEEDED** — 0 errors, 1 pre-existing warning (unrelated)

## Acceptance criteria verification
- [x] AppBar shows user's display name (part before `@`) when logged in — `_displayName = _upn.Split('@')[0]`
- [x] "Admin" chip (Color.Warning = amber) shown for NexusAdmin role — `_isAdmin` guard
- [x] "Reviewer" chip (Color.Info = blue) shown for NexusReviewer role — `else if (_isReviewer)` guard
- [x] No chip shown for regular users — conditional renders nothing
- [x] Chip + name right-aligned in AppBar after `<MudSpacer />`
- [x] Scoped CSS — no inline styles
- [x] No changes to drawer, nav links, or main content

## Known edge cases / things Clint should scrutinize
- `GetUpnAsync()` returns `"unknown"` if no claims match — this will display as `"unknown"` with no chip. Acceptable per spec (regular user = no chip, name shown).
- `_upn` is set to `"unknown"` (non-null), so the `@if (_upn is not null)` block always renders when authenticated. If we want to hide the name for truly unauthenticated/unknown users, we could guard on `_upn != "unknown"` — but that's scope creep; flag if desired.
- `_isReviewer` is set with `!_isAdmin && await IsReviewerAsync()` — prevents double-chip for users with both roles (Admin wins).

## How to test locally
1. `cd ~/projects/fip/nexus && dotnet run --project src/FortressNexus.Web`
2. Log in as a NexusAdmin user → expect amber "Admin" chip + username in AppBar
3. Log in as a NexusReviewer user → expect blue "Reviewer" chip + username
4. Log in as a regular NexusUser → expect only username, no chip
