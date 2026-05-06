# Build Report — ADO#2811

## What was built
NexusAdmin cross-user visibility: admins bypass ownership checks in submission list, submission detail, and the external-dependencies API endpoint. Admins see submitter UPN in the dashboard list. Non-admins are protected from viewing other users' submissions by URL.

## Files changed
- `Services/ISubmissionService.cs` — added `bool isAdmin = false` param to `GetByUserAsync`
- `Services/SubmissionService.cs` — `GetByUserAsync` admin path omits `Where(SubmittedBy == userUpn)` filter; returns all submissions ordered by `SubmittedAt DESC`
- `Components/Pages/Dashboard.razor` — title changes to "All Submissions" for admins; `_isAdmin` passed to `GetByUserAsync`; "Submitter" column (MudText Typo.caption) rendered conditionally for admins showing `SubmittedBy` UPN
- `Controllers/NexusArtifactsController.cs` — `GetExternalDependencies` endpoint now checks `SubmittedBy == currentUpn || NexusRoles.Admin` before proceeding; returns 403 Forbid for unauthorized access (BOLA fix)
- `Components/Pages/SubmissionDetail.razor` — user context (`_currentUserUpn`, `_isAdmin`) resolved first in `LoadSubmissionAsync`; access guard added: non-admins get error message if `SubmittedBy != _currentUserUpn`

## CC sessions run
1 CC session (sonnet) — all 5 files modified in one pass

## Acceptance criteria verification
1. Admin sees all submissions — PASS (GetByUserAsync omits filter when isAdmin=true)
2. Admin can act on any submission — PASS (SubmissionDetail access guard allows admin through; DeleteSubmissionAsync already had callerIsAdmin bypass)
3. Admin sees submitter UPN in list — PASS (Submitter column with MudText Typo.caption, CSS-class-driven)
4. Admin can access external-deps for any submission — PASS (NexusArtifactsController ownership+admin check added)
5. Non-admin restricted to own submissions — PASS (filter preserved when isAdmin=false; SubmissionDetail access guard blocks non-owners)
6. Delete admin bypass continues to work — PASS (DeleteSubmissionAsync unchanged)

## Build
`dotnet build` — 0 errors, 1 pre-existing warning (CS8601 in FileStorageService.cs, unrelated)

## Commit
`7867087` — feat(nexus#2811): add NexusAdmin cross-user visibility

## How to test
1. Log in as NexusAdmin → Dashboard shows all users' submissions with Submitter column
2. Log in as NexusUser → Dashboard shows only own submissions, no Submitter column
3. NexusAdmin hits `/nexus/{other-user-id}` → page loads; NexusUser hits `/nexus/{other-user-id}` → "You don't have permission" error
4. NexusAdmin hits `GET /nexus/{other-user-id}/artifacts/external-dependencies` → 200; NexusUser hits same → 403
