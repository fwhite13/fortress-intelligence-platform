# Build Report — ADO#2826

## What was built
Added `string callerUpn, bool isAdmin = false` ownership check parameters to three `SubmissionService` methods (`UpdateStatusAsync`, `UpdateNarrativeAsync`, `SetActiveSpecDocumentAsync`) as a defense-in-depth measure consistent with `DeleteSubmissionAsync`. Updated `ISubmissionService.cs` and all call sites in `NewSpecWizard.razor`.

## Files changed
- `src/FortressNexus.Web/Services/ISubmissionService.cs` — Updated 3 method signatures to include `string callerUpn, bool isAdmin = false`
- `src/FortressNexus.Web/Services/SubmissionService.cs` — Added new params + ownership guard (`if (!isAdmin && submission.SubmittedBy != callerUpn) throw UnauthorizedAccessException`) to all 3 methods
- `src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` — Updated 7 call sites across `GoToStep2Discovery`, `ApplyResumeChangesAsync`, and `HandleSubmit`; `upn` declared once at top of `HandleSubmit`'s outer try block

## Parallelization used
No — single CC session, no shared-file conflicts.

## CC sessions run
1 CC session (Sonnet), sequential.

## Acceptance criteria verification
- [x] `UpdateStatusAsync` — ownership guard added matching `DeleteSubmissionAsync` pattern
- [x] `UpdateNarrativeAsync` — ownership guard added matching `DeleteSubmissionAsync` pattern
- [x] `SetActiveSpecDocumentAsync` — ownership guard added matching `DeleteSubmissionAsync` pattern
- [x] `ISubmissionService.cs` — interface signatures updated to match
- [x] All 7 `NewSpecWizard.razor` call sites updated with `callerUpn` from `UserContextService.GetUpnAsync()`
- [x] `ArtifactGenerationService` — NOT touched (direct EF entity access, bypasses service layer intentionally)
- [x] Build: **SUCCEEDED** (0 errors, 1 pre-existing warning in unrelated `FileStorageService.cs`)

## Known edge cases / things Clint should scrutinize
- `HandleSubmit` now calls `GetUpnAsync()` at the top of the try block. This covers all 5 `UpdateStatusAsync` and `UpdateNarrativeAsync` calls within that method. No redundant fetches.
- `ApplyResumeChangesAsync` previously had no upn variable — CC correctly added `var upn = await UserContextService.GetUpnAsync()` at the top of that method.
- `SetActiveSpecDocumentAsync` has zero callers currently (service + interface updated prophylactically). No Razor changes needed.
- Callers in wizard always operate on the user's own submission (no admin elevation needed), so `isAdmin: false` default is correct.

## How to test locally
1. `dotnet run` from `src/FortressNexus.Web/`
2. Log in as a normal user, create a submission, step through the wizard — submit should work end-to-end
3. Verify a different user cannot resume/edit another user's submission (ownership guard fires)
4. Log in as NexusAdmin — confirm admin can still operate on any submission by passing `isAdmin: true` when those paths are wired

## Commit
`84442254c1b682de8a12196c562575e28eb1d2db`
