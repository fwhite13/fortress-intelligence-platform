# Build Report — WI #1655: Soft-delete files + narrative update on resume submit

**Commit:** `f2924ec`  
**Branch:** `main`  
**Date:** 2026-04-08  
**Builder:** Tony Stark (software-engineer)  
**CC Model:** Claude Sonnet (CC CLI — `--model sonnet --print --dangerously-skip-permissions`)

---

## CC Invocation

```bash
cd ~/projects/fip/nexus/src/FortressNexus.Web && \
  cat /tmp/1655-cc-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

Brief: `/tmp/1655-cc-brief.md` (10,150 bytes)

---

## Pre-read Investigation Findings

### S3 Delete API Used
`IAmazonS3.DeleteObjectAsync(DeleteObjectRequest)` — the same AWS SDK client already used in `FileStorageService`.

`IFileStorageService` has **no** delete method and was not modified. `IAmazonS3` was injected directly into `SubmissionService` (already registered in DI via `Program.cs`).

### Cascade Behavior: UploadedFile → SubmissionFile
- `UploadedFile → SubmissionFile` FK: **`OnDelete(DeleteBehavior.Restrict)`** — NO cascade
- `Submission → SubmissionFile` FK: `OnDelete(DeleteBehavior.Cascade)`

**Required deletion order:** SubmissionFile junction record(s) first → UploadedFile record second. Both wrapped in individual try/catch blocks so errors are logged Warning without crashing the submit flow.

### Submission.NarrativeText
Confirmed as `string NarrativeText { get; set; }` on the `Submission` entity. No `UpdatedAt` field exists on the entity — only `NarrativeText` is updated.

---

## Files Modified

| File | Changes |
|------|---------|
| `Services/ISubmissionService.cs` | Added `UpdateNarrativeAsync(int, string)` and `DeleteUploadedFileAsync(int, int)` signatures |
| `Services/SubmissionService.cs` | Injected `IAmazonS3`; implemented `UpdateNarrativeAsync` and `DeleteUploadedFileAsync` (S3 non-fatal, junction-first DB delete) |
| `Components/Pages/NewSpecWizard.razor` | Added `ApplyResumeChangesAsync()`; wired into all 3 submit branches; removed all TODO(WI #1655) comments |

---

## Implementation Summary

### New Service Methods

**`UpdateNarrativeAsync(int submissionId, string narrativeText)`**
- Loads `Submission` by ID, sets `NarrativeText`, saves. Logs Warning if not found.

**`DeleteUploadedFileAsync(int submissionId, int fileId)`**
- Step 1: S3 `DeleteObjectAsync` — try/catch, logs Warning on failure, continues
- Step 2: Delete `SubmissionFile` junction records for this submission — try/catch, logs Warning
- Step 3: Delete `UploadedFile` record — try/catch, logs Warning (orphaned record accepted)

### New Wizard Method: `ApplyResumeChangesAsync()`
```csharp
private async Task ApplyResumeChangesAsync()
{
    // 1. Persist updated narrative
    // 2. Delete flagged files from S3 + DB (snapshots _filesToDelete first)
    // 3. Clear _filesToDelete to prevent double-delete
}
```

### HandleSubmit Wiring

| Branch | Action |
|--------|--------|
| `_isResume && _hasChanges && !_regenPending` (first pass) | `ApplyResumeChangesAsync()` called at top, BEFORE SupersedeSession/InitiateDiscovery |
| `_isResume && _hasChanges && _regenPending` (second pass / regen) | `ApplyResumeChangesAsync()` called AFTER `UpdateStatusAsync(Generating)`, BEFORE `GenerateAsync` |
| `_isResume && !_hasChanges && _existingSpecDocument != null` (skip-regen) | `UpdateNarrativeAsync()` called directly (no files to delete), then `UpdateStatusAsync(AwaitingReview)` |

### TODO Comments Resolved
- `// TODO(WI #1655): Delete files marked for removal from S3 + DB here` — removed from `HandleSubmit` first-pass block
- `// TODO(WI #1655): persist updated narrative + file changes before regen` — removed from second-pass block
- `RemoveExistingFile` comment updated to reference `ApplyResumeChangesAsync()`

---

## Build Result

```
dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj
Build succeeded.
    0 Error(s)
    0 Warning(s)
```

**Commit:** `f2924ec` — `feat(nexus#1655): soft-delete files + persist narrative on resume submit`

---

## Things to Scrutinize in Review

1. **`ApplyResumeChangesAsync` called in first-pass before re-discovery** — this means files are hard-deleted at first Submit click when changes are detected, before the user even completes the new discovery round. If the user navigates away mid-discovery, files are already gone. Acceptable per spec, but worth a UX consideration note.

2. **`_filesToDelete.Clear()` in `ApplyResumeChangesAsync`** — on second pass (regen), `_filesToDelete` will already be empty (cleared on first pass). The method is safe to call idempotently.

3. **S3 orphan scenario** — if S3 delete succeeds but DB delete fails, the S3 object is gone but the `UploadedFile` record remains. Logged as Warning. No cleanup path currently. Acceptable per spec constraints.

4. **Skip-regen path calls `UpdateNarrativeAsync` directly** — not via `ApplyResumeChangesAsync`, because `_filesToDelete` is empty when `_hasChanges == false`. This is correct and intentional.
