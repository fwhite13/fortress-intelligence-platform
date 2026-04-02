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

---

## BUILD cycle 3 — ADO#1554: MSAL → FIP cookie consumer

### What was built
Ripped out standalone Entra MSAL/OIDC auth (`AddMicrosoftIdentityWebAppAuthentication`) and replaced with the FIP shared cookie consumer pattern (exact match to FIRM/FORMS). NEXUS no longer owns its own auth — it reads the `.FortressAI.Session` cookie set by fip.fortressam.ai at login.

### Files changed
- `src/FortressNexus.Web/Program.cs` — Removed `Microsoft.Identity.Web` usings, `AddMicrosoftIdentityWebAppAuthentication`, `Configure<CookieAuthenticationOptions>` domain block, and `AddMicrosoftIdentityUI`. Added FIP shared cookie auth block (`AddAuthentication/AddCookie`) with `.FortressAI.Session`, `Auth__CookieDomain`, `LoginPath=/auth/redirect-to-login`. Updated `/auth/redirect-to-login` to pass `returnUrl`. Updated `MapControllers()` comment.
- `src/FortressNexus.Web/appsettings.json` — Removed `AzureAd` section. Added `FIP.LoginUrl`.
- `src/FortressNexus.Web/FortressNexus.Web.csproj` — Removed `Microsoft.Identity.Web` and `Microsoft.Identity.Web.UI` package references.

### Parallelization used
No — all changes in related files, sequential CC run.

### CC sessions run
1 × CC Sonnet via pipe mode. Clean first pass.

### Acceptance criteria verification
- [x] No `Microsoft.Identity.Web` usings in Program.cs — verified
- [x] No `AddMicrosoftIdentityWebAppAuthentication` in Program.cs — verified
- [x] No `AddMicrosoftIdentityUI` in Program.cs — verified
- [x] Cookie auth block matches FIRM pattern: `.FortressAI.Session`, `Auth__CookieDomain`, LoginPath `/auth/redirect-to-login` — verified
- [x] `/auth/redirect-to-login` passes `returnUrl` to FIP LoginUrl — verified
- [x] `app.MapControllers()` kept (SubmissionExportController — ADO#1526) — verified
- [x] appsettings.json: `AzureAd` section removed — verified
- [x] appsettings.json: `FIP.LoginUrl` present — verified
- [x] `Microsoft.Identity.Web` packages removed from .csproj — verified (grep returns nothing)
- [x] Build: **SUCCEEDED** — 0 errors, 0 warnings

### Known edge cases / things Clint should scrutinize
- `Azure.Identity` and `Azure.Extensions.AspNetCore.Configuration.Secrets` packages remain — they're used for KeyVault integration, not auth. Correct to keep.
- `builder.Services.AddControllersWithViews()` remains (was `.AddMicrosoftIdentityUI()` chained to it before) — this is fine; it supports `MapControllers()`.
- `Auth__CookieDomain` (double underscore) matches ECS env var naming convention used across FIP. Consistent with FIRM.

### How to test locally
```bash
cd /home/fredw/projects/fip/nexus
dotnet run --project src/FortressNexus.Web/FortressNexus.Web.csproj
# No valid .FortressAI.Session cookie → should redirect to https://fip.fortressam.ai?returnUrl=...
# With valid shared cookie from FIP → should load dashboard directly
```

### Commit
`6a0ec0f fix(ADO#1554): replace standalone Entra MSAL auth with FIP shared cookie consumer pattern`
