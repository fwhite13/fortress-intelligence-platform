# Build Report — WI #1654: Pre-populate narrative + existing files in resume mode

**Date:** 2026-04-08  
**Engineer:** Tony Stark (software-engineer)  
**Commit:** `41b49ca`  
**Build:** ✅ 0 errors, 0 warnings

---

## CC Invocation

```bash
cd /home/fredw/projects/fip/nexus && cat /tmp/tony-brief-1654.md | claude --model sonnet --print --dangerously-skip-permissions
```

One CC run. Sonnet. No fallback needed.

---

## What Was Built

Three UI features wired up in `NewSpecWizard.razor` for resume mode:

### 1. Narrative pre-population
**Field:** `_narrativeText` (the `@bind-Value` on the narrative MudTextField in Step 0)

WI #1653 already assigns `_narrativeText = submission.NarrativeText` in `OnInitializedAsync`. Since the field binds directly to `_narrativeText`, it renders pre-filled with no additional changes required. The field remains editable — user can modify before submitting. This is zero-change, already working.

### 2. Existing files display
**Where:** Step 1 (Files step, `_activeStep == 1`), rendered inline above the `<FileUploadZone>` component.

When `_isResume == true` and `_uploadedFiles.Count > 0`, a `MudList` renders existing files filtered by `_filesToDelete`. Each item shows `OriginalFileName` and a × `MudIconButton`. The `FileUploadZone` drag-drop area sits below — fully active for adding new files.

No new component was created. The list is inline markup in the wizard.

### 3. `_filesToDelete` soft-delete state
**Type:** `HashSet<int>` (keyed on `UploadedFile.Id`)

- Added field: `private HashSet<int> _filesToDelete = new();`
- Added method: `RemoveExistingFile(int fileId)` — adds ID to set, no DB/S3 calls
- Optimistic UI: the `@foreach` filters with `.Where(f => !_filesToDelete.Contains(f.Id))` so the file disappears immediately on click
- Actual deletion deferred to submit (WI #1655)

### 4. Review step file count
Added `TotalFileCount` computed property:
```csharp
private int TotalFileCount =>
    (_isResume ? _uploadedFiles.Count(f => !_filesToDelete.Contains(f.Id)) : 0) + _pendingFiles.Count;
```
Step 3 review now shows `TotalFileCount` so the count reflects existing (minus soft-deleted) + new pending files.

---

## Files Modified

| File | Changes |
|------|---------|
| `Components/Pages/NewSpecWizard.razor` | `_filesToDelete` field, `TotalFileCount` property, `RemoveExistingFile` method, Step 1 existing files list, Step 3 file count update |

`FileUploadZone.razor` — **not modified**. No new parameters needed.

---

## One CC Fix

The brief specified `FileName` but the `UploadedFile` entity property is `OriginalFileName`. CC caught this and used the correct property name before the build.

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:05.68
```

---

## Things Clint Should Scrutinize

1. **`_filesToDelete` is not persisted** — if the user navigates away mid-resume without submitting, soft-deletes are lost (intentional, WI #1655 handles actual deletion on submit). No state leak.
2. **New files during resume** — go through the existing `GoToStep2Discovery` upload path (`_pendingFiles` → `_uploadedFiles`). These new files are associated with the submission immediately on step advance. Abandoned uploads are accepted orphan risk (out of scope).
3. **`_isResume` guard** — all new logic is behind `_isResume == true`. New-submission path (`ResumeSubmissionId == null`) is completely untouched.
4. **Loop variable capture** — `var capturedFile = existingFile` correctly captures for the closure in `OnClick="@(() => RemoveExistingFile(capturedFile.Id))"`. No loop-capture bug.
