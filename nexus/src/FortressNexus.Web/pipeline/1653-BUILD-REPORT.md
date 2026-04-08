# Build Report — WI #1653
## NewSpecWizard ResumeSubmissionId parameter + on-init load

**Date:** 2026-04-08  
**Agent:** Tony Stark (software-engineer)  
**Commit:** `ad07edf`

---

## CC Invocation

```bash
cd ~/projects/fip/nexus/src/FortressNexus.Web && \
  cat /tmp/1653-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

## Route Decision

**Chosen:** `@page "/nexus/{ResumeSubmissionId:int}/resume"`

**Rationale:**
- Clean, RESTful, and matches the existing `@page "/nexus/{Id:int}"` detail pattern
- The `:int` constraint matches `Submission.Id` type exactly
- No query-string parsing needed — Blazor router handles the parameter binding directly
- Avoids route collision: `/nexus/{Id:int}` → detail, `/nexus/{Id:int}/resume` → resume wizard
- `SubmissionDetail.razor` WI #1650 "Continue Submission" button should link to `/nexus/{Id}/resume`

---

## Files Modified

| File | Change |
|------|--------|
| `Services/UserContextService.cs` | Added `IsAdminAsync()` — checks `NexusRoles.Admin` role via `_authStateProvider` |
| `Components/Pages/NewSpecWizard.razor` | Added resume route directive, `[Parameter] ResumeSubmissionId`, 4 state fields, `OnInitializedAsync` with full resume load logic |

---

## Service Methods Added/Modified

| Method | File | Change |
|--------|------|--------|
| `UserContextService.IsAdminAsync()` | `Services/UserContextService.cs` | **New** — returns `bool` indicating user has `NexusAdmin` role |

**No changes to `ISubmissionService` or `IDiscoveryService`** — existing methods covered all needs:
- `ISubmissionService.GetByIdAsync(int id)` — already loads SubmissionFiles.UploadedFile + SpecDocuments
- `IDiscoveryService.GetSessionAsync(int submissionId)` — already exists from Phase 2

---

## New State Fields

```csharp
[Parameter] public int? ResumeSubmissionId { get; set; }
private bool _isResume = false;                    // flag — consumed in WI #1656
private string _originalNarrative = string.Empty; // baseline for change detection (WI #1656)
private HashSet<int> _originalFileIds = new();    // baseline for change detection (WI #1656)
private SpecDocument? _existingSpecDocument;       // latest spec doc if any exists
```

**Note:** `_originalFileIds` is `HashSet<int>` (not `HashSet<string>`) — `UploadedFile.Id` and `SubmissionFile.UploadedFileId` are both `int`. The MEMORY.md string-ID note applies to Guid columns via Pomelo, not int PKs.

---

## Resume Logic Summary

1. If `ResumeSubmissionId` is null → return immediately; new-submission flow unchanged
2. Load submission via `GetByIdAsync` → 404 guard → snackbar + redirect to /nexus
3. Auth guard: owner (`submission.SubmittedBy == currentUpn`) OR `NexusAdmin` → else snackbar + redirect
4. Populate `_title`, `_featureArea`, `_narrativeText`, `_submissionId` from submission
5. Load `_uploadedFiles` from `SubmissionFiles` junction (ordered by SortOrder)
6. Load `_discoverySession` if `submission.DiscoveryStatus` is set (non-fatal try/catch)
7. Load `_existingSpecDocument` (latest by version, nullable)
8. Set `_isResume = true`
9. Snapshot `_originalNarrative` and `_originalFileIds` — never modified after init

---

## dotnet build Result

```
Build succeeded.
  1 Warning(s)  — CS0414: _isResume assigned but not yet read (expected; WI #1656 consumes it)
  0 Error(s)
```

---

## Acceptance Criteria Verification

- [x] `[Parameter] public int? ResumeSubmissionId` exists on wizard
- [x] `_isResume`, `_originalNarrative`, `_originalFileIds` fields present
- [x] `OnInitializedAsync` loads submission + files + discovery + spec when `ResumeSubmissionId` is set
- [x] Auth guard: owner or NexusAdmin; else redirect `/nexus` with warning toast
- [x] New-submission flow (null `ResumeSubmissionId`) completely unchanged
- [x] Route `/nexus/{id:int}/resume` established and documented
- [x] `dotnet build` — 0 errors

---

## Known Edge Cases / Things Clint Should Scrutinize

1. **CS0414 warning on `_isResume`** — expected/intentional; WI #1656 adds the conditional UI that reads it. Suppress only after #1656 ships.
2. **Discovery session load guard** — checks `submission.DiscoveryStatus` string. If DiscoveryStatus is null/empty, session load is skipped entirely (safe). If the session DB row exists but DiscoveryStatus wasn't set, the session won't load on resume — acceptable for this WI.
3. **`_pendingFiles` starts empty on resume** — correct behavior. `_uploadedFiles` holds the DB-loaded files; `_pendingFiles` is for new browser files added during the current session. No conflict.
4. **`SubmissionDetail.razor` linking** — WI #1650 must use `/nexus/{Id}/resume` for the "Continue Submission" button. This WI establishes that contract.

---

## Git Commit

```
ad07edfc4eb59b69ca10a93f13ad548b704ca576
feat(nexus#1653): add ResumeSubmissionId route + OnInitializedAsync resume-mode load to NewSpecWizard
```
