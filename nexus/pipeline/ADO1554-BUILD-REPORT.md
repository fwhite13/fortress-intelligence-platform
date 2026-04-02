# Build Report — ADO#1554: NexusDashboard

## What was built
Replaced the scaffold `Dashboard.razor` with a fully functional NEXUS Dashboard at route `/`. Implements user submission list with status badges, empty state, loading state, and an admin-only Pending Review section.

## Files changed
- `src/FortressNexus.Web/Components/Pages/Dashboard.razor` — Complete replacement of scaffold. Added MudTable submission list, MudChip status badges, empty state MudAlert, loading MudProgressLinear, NexusAdmin Pending Review section (second table), and "New Submission" button. All CSS classes follow `nexus-dashboard-*` naming convention.

## Parallelization used
No — single file, single CC session.

## CC sessions run
1 × CC Sonnet. Brief piped directly. Output was clean first pass.

## Acceptance criteria verification
- [x] Route `@page "/"` — verified in file header
- [x] `@attribute [Authorize]` — present
- [x] `@rendermode InteractiveServer` — present
- [x] Header: MudText h4 "My Submissions" + "New Submission" button → `/nexus/new` — verified
- [x] MudTable with 6 columns: #, Title, Feature Area, Status, Submitted, Action — verified
- [x] Title column: MudLink → `/nexus/{id}` — verified
- [x] Feature Area: nullable, shows "—" if null — `@(context.FeatureArea ?? "—")` verified
- [x] Status badge: MudChip with `GetStatusColor()` — exact mapping from SubmissionDetail.razor copied
- [x] Submitted: `ToString("MMM d, yyyy")` — verified
- [x] Action: "View" MudButton → `/nexus/{id}` — verified
- [x] Empty state: MudAlert with correct text — verified
- [x] Loading state: MudProgressLinear while `_loading` — verified
- [x] NexusAdmin Pending Review section: guarded by `_isAdmin`, uses `GetAllPendingReviewAsync()` — verified
- [x] Build: **SUCCEEDED** — 0 warnings, 0 errors
- [x] Commit: `246dd0d`

## Status color mapping (from SubmissionDetail.razor — verified exact match)
| Status | Color |
|--------|-------|
| Draft | Color.Default |
| Pending | Color.Info |
| Generating | Color.Warning |
| AwaitingReview | Color.Primary |
| Approved | Color.Success |
| Failed | Color.Error |
| _ (default) | Color.Default |

## Known edge cases / things Clint should scrutinize
- `ArtifactsCreated` status (exists in enum, not in spec's color table) — falls through to `_ => Color.Default`. This matches SubmissionDetail.razor behavior; no change needed unless a distinct color is wanted.
- `userUpn` is sourced from `authState.User.Identity?.Name` — consistent with rest of codebase (UserContextService pattern). Empty string fallback means `GetByUserAsync("")` returns empty list rather than throwing.
- Admin pending review list could include the admin's own submissions. By design — mirrors the review queue view.

## How to test locally
```bash
cd /home/fredw/projects/fip/nexus
dotnet run --project src/FortressNexus.Web/FortressNexus.Web.csproj
# Navigate to / — should show My Submissions table or empty state
# As NexusAdmin user — should show Pending Review section below
```

## Commit
`246dd0d feat(ADO#1554): NexusDashboard — submission list with status badges`
