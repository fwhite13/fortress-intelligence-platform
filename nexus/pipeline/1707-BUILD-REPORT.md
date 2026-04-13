# Build Report — NEXUS WI #1707
**Resume file upload: new files not persisted + dual list rendering**

---

## What was built

Fixed two related bugs in resume mode of `NewSpecWizard.razor`:
1. New files added during resume are now immediately uploaded to S3 and persisted with a `SubmissionFile` junction record
2. Existing (DB) and pending (new) files now render as a single unified "Attached Files" list

---

## Root Causes Found

### Issue 1 — New files not persisted
`HandleFilesSelected` only buffered files into `_pendingFiles = files.ToList()` — no upload, no DB write.

The upload block in `GoToStep2Discovery()` was guarded by `if (_submissionId == null && _pendingFiles.Count > 0)`. In resume mode, `_submissionId` is always populated (loaded in `OnInitializedAsync`), so this block was **entirely skipped**. Files were held in memory only and lost on any navigation.

Additionally, even if the upload ran, there was no method to create a `SubmissionFile` junction record for an **existing** submission — `CreateAsync` handles this for new submissions only.

### Issue 2 — Dual file list rendering
Step 1 (Files) had two completely separate rendering sections:
- A conditional `MudList` for `_uploadedFiles` (DB-loaded, shown only in resume) labeled "Previously Uploaded Files"
- `FileUploadZone` with `InitialFiles="@(_pendingFiles.AsReadOnly())"` rendering its own internal list for newly selected files

These rendered as two independent visual sections with separate headings and remove UX.

---

## Exact Fixes Applied

### `ISubmissionService.cs`
- Added `Task AddFileToSubmissionAsync(int submissionId, int uploadedFileId, int sortOrder)`

### `SubmissionService.cs`
- Implemented `AddFileToSubmissionAsync` — creates a `SubmissionFile` junction record linking an already-uploaded file to an existing submission

### `NewSpecWizard.razor` — 3 changes

**1. `HandleFilesSelected` → async with resume-mode immediate upload:**
- In resume mode (`_isResume && _submissionId.HasValue`): immediately calls `FileStorageService.UploadAsync`, `SubmissionService.SaveUploadedFileAsync`, and `SubmissionService.AddFileToSubmissionAsync` per file. Newly uploaded files go into `_uploadedFiles` (not `_pendingFiles`) — they are now persisted.
- In new-submission mode: unchanged — buffers to `_pendingFiles` for batch upload at `GoToStep2Discovery`.

**2. Step 1 markup → unified file list:**
- Removed the separate "Previously Uploaded Files" `MudList` block (resume-only, showed `_uploadedFiles`)
- Added a single unified "Attached Files" list that renders both `visibleExisting` (`_uploadedFiles` filtered by `_filesToDelete`) and `_pendingFiles` in one `MudList`
- Removed `InitialFiles` parameter from `FileUploadZone` — the zone is now a pure upload trigger only; its internal list no longer renders anything (empty `_selectedFiles`)

**3. `_hasChanges` detection:**
- Changed from `_pendingFiles.Count > 0` to `_uploadedFiles.Any(f => !_originalFileIds.Contains(f.Id))`
- Required because resume uploads now go to `_uploadedFiles`, not `_pendingFiles`

---

## Build Result

```
dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj
Build succeeded.
0 Error(s)
```

**Commit:** `795bd15` — `fix(#1707): resume file upload persistence + unified file list`

---

## Files Changed

| File | Change |
|------|--------|
| `src/FortressNexus.Web/Services/ISubmissionService.cs` | Added `AddFileToSubmissionAsync` to interface |
| `src/FortressNexus.Web/Services/SubmissionService.cs` | Implemented `AddFileToSubmissionAsync` |
| `src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` | HandleFilesSelected async + resume upload; unified file list markup; _hasChanges fix |

---

## Known Edge Cases / Things to Scrutinize

- **Sort order in resume uploads:** Sort order is calculated as `_uploadedFiles.Count(f => !_filesToDelete.Contains(f.Id)) + _pendingFiles.Count` at upload time. Since resume uploads clear `_pendingFiles` immediately, `_pendingFiles.Count` will always be 0 in resume mode — this is correct, just worth noting.
- **File count limit:** `FileUploadZone` enforces max 10 files internally, but there is no cross-check between the zone's selection count and the number of already-persisted files in resume mode. If a user has 8 existing files and tries to upload 5 new ones, the zone accepts up to 10 new selections. The 10-file guard is per-selection-event, not cumulative across existing files. Pre-existing behavior — not introduced by this fix.
- **`_pendingFiles` in new-submission mode:** `FileUploadZone` still renders its internal file list for new-submission mode (since `InitialFiles` is no longer passed). New-submission mode now has a visual asymmetry: existing files show in the unified list above the zone, but newly selected files show BOTH in the unified list (via `_pendingFiles` loop) AND in the zone's internal list. Consider whether the unified list loop for `_pendingFiles` should be new-submission only, or if `FileUploadZone`'s internal list should be suppressed in new-submission mode too. For the scope of this WI, the primary issue (resume mode) is fixed; new-submission visual polish is out of scope.

---

## How to Test

1. Create a new submission through the full wizard and submit
2. Find it on `/nexus`, click Resume
3. On Step 1 (Files): verify existing files show in "Attached Files" list
4. Add a new file — it should appear in the same "Attached Files" list immediately (no separate section)
5. Navigate back to Step 1 (Details) and return to Step 1 (Files) — new file should still appear (it is now persisted)
6. Submit — verify no file data loss

---

## Cycle 2 — Race guard + dual-render fix

**Commit:** `e39851c` — `fix(#1707): C2 race guard in HandleFilesSelected + restore InitialFiles for new-submission mode`

### C1 — Race window in `HandleFilesSelected`

**Problem:** `_isUploading = true` was set AFTER `await UserContextService.GetUpnAsync()` in the resume branch. Blazor Server yields on every `await`, so a second file-select event could enter the method concurrently → duplicate S3 uploads + duplicate `SubmissionFile` junction records.

**Fix (3 lines, surgical):**
- Added `if (_isUploading) return;` as the very first line of the method body
- Moved `_isUploading = true;` immediately after the guard, before any `await`
- Wrapped entire method body in `try { ... } finally { _isUploading = false; }` to guarantee reset

### C2 — Dual rendering in new-submission mode

**Problem:** `InitialFiles` was removed from `FileUploadZone` in Cycle 1. This left `FileUploadZone` rendering its own internal `_selectedFiles` list after user picks files, while the unified list above also rendered `_pendingFiles`. Same files appeared twice in new-submission mode.

**Fix:**
- Restored `InitialFiles="@(_isResume ? null : _pendingFiles.AsReadOnly())"` on `<FileUploadZone>` — in new-submission mode, the zone seeds its own internal list from `_pendingFiles`; in resume mode, null keeps zone list empty
- Changed unified list condition from `_pendingFiles.Count > 0` to `_isResume && _pendingFiles.Count > 0`
- Wrapped `_pendingFiles` foreach loop in `@if (_isResume)` — in new-submission mode, pending files render only inside the zone; in resume mode, they render only in the unified list

**Result:** Each file appears exactly once regardless of mode.

### Build Result
```
Build succeeded.
0 Warning(s)
0 Error(s)
```

### Files Changed (Cycle 2)
| File | Change |
|------|--------|
| `src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` | Race guard + flag hoist in HandleFilesSelected; InitialFiles restored on FileUploadZone; _pendingFiles loop gated to resume mode |
