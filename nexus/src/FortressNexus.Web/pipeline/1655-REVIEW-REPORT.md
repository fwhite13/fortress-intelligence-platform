# Review Report — WI #1655: File Hard-Delete + Narrative Persist on Resume Submit

**Verdict: ✅ PASS**
**Cycle:** 1 of 2
**Reviewer:** Hawkeye (code-reviewer)
**Commit:** `f2924ec`
**Risk:** Medium — S3 delete + EF cascade

---

## CC Review Summary

CC reviewed all 11 checks across 4 files. All critical and important checks passed. Two non-blocking observations noted. No false positives dismissed — all findings are genuine.

---

## Spec Compliance Check

**§ Files Modified:**
- `Services/ISubmissionService.cs` — ✅ new method signatures added
- `Services/SubmissionService.cs` — ✅ new methods implemented
- `Components/Pages/NewSpecWizard.razor` — ✅ `ApplyResumeChangesAsync` + all 3 wiring points

**§ Acceptance Criteria:**
- [x] `UpdateNarrativeAsync` implemented and wired ✅
- [x] `DeleteUploadedFileAsync` implemented with correct cascade order ✅
- [x] S3 delete non-fatal ✅
- [x] `ApplyResumeChangesAsync` called in all 3 resume branches ✅
- [x] All `TODO(WI #1655)` comments resolved ✅

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Files Cross-Referenced:**
- `Services/SubmissionService.cs` ↔ `Services/ISubmissionService.cs` — ✅ signatures match
- `Models/Entities/UploadedFile.cs` `S3Key`/`S3Bucket` ↔ `DeleteUploadedFileAsync` usage — ✅ fields exist and are loaded
- `Models/Entities/SubmissionFile.cs` FK structure ↔ delete order in service — ✅ junction deleted before parent
- `Program.cs` IAmazonS3 registration (`AddSingleton`) ↔ MEMORY.md anti-pattern check — ✅ **Singleton** (correct)

**Undocumented Dependencies Found:**
- `SubmissionService` constructor now takes `IAmazonS3` — confirmed registered as Singleton in Program.cs line 129
- `DeleteUploadedFileAsync` uses `.Include(f => f.SubmissionFiles)` — needed for the junction delete step; correct

---

## Critical Issues: 0

### ✅ C1: Delete order — PASS
`DeleteUploadedFileAsync` order: (1) S3 try/catch, (2) SubmissionFile junction remove + SaveChanges, (3) UploadedFile remove + SaveChanges. FK-safe. Restrict cascade respected.

### ✅ C2: S3 non-fatal try/catch scope — PASS
S3 try/catch wraps only the S3 call. DB delete steps are in independent try/catch blocks. S3 exception cannot abort DB deletion.

### ✅ C3: `_filesToDelete.Clear()` after loop — PASS
`_filesToDelete.Clear()` is on line 479, after the foreach loop (lines 473–476). Snapshot (`toDelete = _filesToDelete.ToList()`) used for iteration. Correct.

### ✅ C4: Narrative update in all 3 branches — PASS

| Branch | Condition | Call | Verified |
|--------|-----------|------|----------|
| Pass 1 regen | `_isResume && _hasChanges && !_regenPending` | `ApplyResumeChangesAsync()` | ✅ |
| Pass 2 regen | `_isResume && _hasChanges && _regenPending` | `ApplyResumeChangesAsync()` before `GenerateAsync` | ✅ |
| Skip-regen | `_isResume && !_hasChanges && _existingSpecDocument != null` | `UpdateNarrativeAsync()` directly | ✅ |

### ✅ C5: `_filesToDelete` scope — newly uploaded files — PASS
`RemoveExistingFile()` only called from the UI loop over `_uploadedFiles` (DB-sourced). `HandleFilesSelected()` only sets `_pendingFiles`. No path allows a `_pendingFile` to enter `_filesToDelete`.

---

## Important Issues: 0

### ✅ I1: S3 key retrieval — PASS
`DeleteUploadedFileAsync` loads `UploadedFile` with `.Include(f => f.SubmissionFiles)` before any delete step. `S3Key` and `S3Bucket` come from the loaded entity. Empty/null key would cause a silent S3 no-op (caught), DB delete proceeds — acceptable.

### ✅ I2: IAmazonS3 DI — PASS
`Program.cs:129`: `builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client())`. Singleton — matches MEMORY.md anti-pattern requirement. No socket exhaustion risk.

### ✅ I3: Orphaned file logging — PASS
On S3 failure, `LogWarning` includes `fileId` and `uploadedFile.S3Key`. Sufficient for manual cleanup.

---

## Nitpicks: 2

**N1:** S3 failure log omits bucket name (`S3Bucket`). Log includes key but not bucket. In a multi-bucket setup, the key alone may be ambiguous for manual cleanup. Add `bucket={Bucket}` to the warning. Not blocking.

```csharp
// Current:
_logger.LogWarning(ex, "[SUBMISSION] S3 delete failed for fileId={FileId} key={Key} — ...", fileId, uploadedFile.S3Key);
// Suggested:
_logger.LogWarning(ex, "[SUBMISSION] S3 delete failed for fileId={FileId} bucket={Bucket} key={Key} — ...", fileId, uploadedFile.S3Bucket, uploadedFile.S3Key);
```

**N2:** In Pass 2, `ApplyResumeChangesAsync` is effectively a no-op for file deletions (already cleared in Pass 1) but still correct — it handles the edge case where the user modifies narrative/files between Pass 1 and Pass 2. A comment would clarify intent for future readers. Not blocking.

---

## Additional Adversarial Checks

### ✅ Pass 2 ordering and error recovery
Order: `UpdateStatusAsync(Generating)` → `ApplyResumeChangesAsync()` → `GenerateAsync()`. `ApplyResumeChangesAsync` swallows its own exceptions, so it won't throw to the outer catch — acceptable since all sub-operations are individually non-fatal by design.

### ✅ Snapshot mutation safety
`_filesToDelete.ToList()` snapshot used for iteration. Blazor Server circuit dispatcher prevents concurrent mutation anyway. Belt-and-suspenders correct.

### ✅ `_hasChanges` guards skip-regen path
`_hasChanges` includes `_filesToDelete.Count > 0`. If `_filesToDelete` is non-empty, `_hasChanges` is `true` and the skip-regen path (`!_hasChanges`) cannot be entered. No hidden data loss path.

---

## Positive Observations

- Three-block try/catch pattern in `DeleteUploadedFileAsync` is clean — S3, junction, parent each isolated. Maximum resilience without complexity.
- `_filesToDelete.ToList()` snapshot before loop is a good defensive practice even in single-threaded Blazor.
- `_hasChanges` computed property correctly includes all three change signals (narrative diff, file deletions, pending files). No path to enter skip-regen with pending changes.
- S3 registered as Singleton — correct and consistent with MEMORY.md convention.

---

## What to Fix

Nothing required. Two nitpicks above are optional improvements.

---

_Reviewed: 2026-04-08 | Hawkeye | Cycle 1 of 2_
