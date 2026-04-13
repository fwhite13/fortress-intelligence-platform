# Review Report — NEXUS #1707
**Resume file upload persistence + unified file list**
**Reviewer:** Hawkeye | **Cycle:** 1 | **Date:** 2026-04-13

---

## Verdict: NEEDS-CHANGES

Two issues require fixes before merge. All other paths verified correct.

---

## Spec Compliance Check

No formal developer brief was issued for this WI — this was a direct bug fix, not a spec-driven feature. Review evaluated against the build report's stated root causes, fixes, and acceptance criteria.

**§ Files Changed (per build report):**
- `Services/ISubmissionService.cs` — ✅ modified
- `Services/SubmissionService.cs` — ✅ modified
- `Components/Pages/NewSpecWizard.razor` — ✅ modified
- `pipeline/1707-BUILD-REPORT.md` — ✅ present (in separate commit 14f2671)

**§ Scope:**
- The fix commit (`795bd15`) also bundles FAIT pipeline reports (`1667-*`, `1668-*`, `1669-*`, `1670-*`). These are pre-existing pipeline docs that appear to have been uncommitted at the time. No code changes outside scope. Minor commit hygiene issue only — not blocking.

**§ Acceptance Criteria (from build report):**
- [x] New files in resume mode are uploaded to S3 ✅
- [x] `SubmissionFile` junction records created for new files ✅
- [x] Files render in unified "Attached Files" list ✅ (with caveat — see C4)
- [ ] Clean separation of display between resume and new-submission mode ❌ (dual-render bug — see C4)

---

## Consistency Audit

**Checked:**
- `HandleFilesSelected` → `FileStorageService.UploadAsync` → `SubmissionService.SaveUploadedFileAsync` → `SubmissionService.AddFileToSubmissionAsync` call chain: ✅ consistent
- `AddFileToSubmissionAsync` pattern vs. `CreateAsync` junction creation: ✅ matches pattern
- `_originalFileIds` snapshot (init) vs. `_hasChanges` detection (`_uploadedFiles.Any(f => !_originalFileIds.Contains(f.Id))`): ✅ consistent
- `visibleExisting` (render) vs. `_filesToDelete` (delete set): ✅ consistent
- `FileUploadZone` `InitialFiles` parameter removed from call site: ✅ parameter exists but no longer passed

---

## Critical Issues — 2 found

### C1: Race window in `HandleFilesSelected` — `_isUploading` guard set too late

- **File:** `Components/Pages/NewSpecWizard.razor` (~line 485)
- **Category:** Correctness / Concurrency
- **Severity:** Critical (can produce duplicate S3 uploads and duplicate junction records)

**Issue:** In Blazor Server, when an `async` event handler yields at `await`, the circuit dispatcher may process another queued event before the first resumes. `_isUploading = true` is currently set *after* the first `await`:

```csharp
private async Task HandleFilesSelected(IReadOnlyList<IBrowserFile> files)
{
    if (_isResume && _submissionId.HasValue)
    {
        var upn = await UserContextService.GetUpnAsync();  // ← yield point
        _isUploading = true;   // ← guard set AFTER yield — second call can enter here
        StateHasChanged();
        ...
    }
}
```

If a user triggers a second file-selection event (e.g., the FileUploadZone fires `OnFilesSelected` twice — once on select, once if they interact again before upload completes), the second call enters the resume branch while `_isUploading` is still `false`. Both calls loop concurrently through their `files` list, making interleaved calls to `UploadAsync`, `SaveUploadedFileAsync`, and `AddFileToSubmissionAsync`, and both call `_uploadedFiles.Add(saved)` with incorrect sort orders.

In practice, `GetUpnAsync()` is likely fast (cached UPN), making this extremely low probability. But the window exists architecturally.

**Fix:**
```diff
-    if (_isResume && _submissionId.HasValue)
-    {
-        var upn = await UserContextService.GetUpnAsync();
-        _isUploading = true;
-        StateHasChanged();
+    if (_isResume && _submissionId.HasValue)
+    {
+        if (_isUploading) return;   // guard against re-entry
+        _isUploading = true;
+        StateHasChanged();
+        var upn = await UserContextService.GetUpnAsync();
```

Or equivalently, move `_isUploading = true` to be the first statement in the resume branch (before any await).

---

### C2: Dual rendering in new-submission mode — files shown twice

- **File:** `Components/Pages/NewSpecWizard.razor` (Step 1 markup, ~lines 86-125) + `Components/Shared/FileUploadZone.razor`
- **Category:** UX/Correctness
- **Severity:** Critical (user-visible broken behavior, though no data impact)

**Issue:** In new-submission mode, when a user selects files:
1. `FileUploadZone.HandleFilesChanged` sets its own internal `_selectedFiles` list and renders them (file name + size + remove button) in its own `MudList`
2. `OnFilesSelected.InvokeAsync` → `HandleFilesSelected` → `_pendingFiles = files.ToList()` → wizard re-renders its `_pendingFiles` loop

Both lists are simultaneously visible. The same files appear twice: once inside the FileUploadZone widget with the full UI (icon + size + remove button), and once in the unified "Attached Files" section above the zone (name only, no remove). The user has two separate remove controls for the same file with different UX, and it's unclear which is authoritative.

The prior implementation avoided this by passing `InitialFiles="@(_pendingFiles.AsReadOnly())"` — the FileUploadZone would pick up the same list and show it internally (avoiding duplication). The fix removed `InitialFiles` but did not suppress FileUploadZone's own render.

**Functional impact:** None. No duplicate uploads. `GoToStep2Discovery` uses `_pendingFiles` as source. FileUploadZone's remove button invokes `HandleFilesSelected` with the reduced list, keeping `_pendingFiles` in sync. No orphaned state.

**Two viable fixes:**

Option A — Conditionally suppress `_pendingFiles` in the wizard's unified list for new-submission mode (simplest):
```diff
- @foreach (var pendingFile in _pendingFiles)
+ @if (_isResume) { @foreach (var pendingFile in _pendingFiles) {
```
Let FileUploadZone continue to render its own list for new-submission mode. The unified section only shows pending files in resume mode (which is where they appear in `_uploadedFiles`, not `_pendingFiles`, so this would be empty anyway — but make it explicit).

Wait — in resume mode, `HandleFilesSelected` clears `_pendingFiles` and puts uploads into `_uploadedFiles`. So `_pendingFiles` is always empty in resume mode after an upload. The `_pendingFiles` loop in the unified list only ever renders in new-submission mode. Which is the dual-render case.

Actually cleaner fix:

Option B — Remove the `_pendingFiles` loop from the unified section entirely, and re-pass `InitialFiles` to FileUploadZone for new-submission mode only:
```diff
+ <FileUploadZone OnFilesSelected="HandleFilesSelected" InitialFiles="@(_isResume ? null : _pendingFiles.AsReadOnly())" />
- <FileUploadZone OnFilesSelected="HandleFilesSelected" />
```
And remove the `_pendingFiles` loop from the unified section. FileUploadZone handles its own list display in new-submission mode. The unified list only renders `visibleExisting` (resume files).

Recommend Option B as it restores the original intent and eliminates the duplication at source.

---

## Important Issues — 0 found

### I-check 1: `_submissionId` null guard ✅

No code path exists where `_isResume = true` and `_submissionId = null`. In `OnInitializedAsync`, `_submissionId` is assigned at line 308 before `_isResume = true` at line 336. All early-exit paths (not found, auth failure) return before either field is set. The `if (_isResume && _submissionId.HasValue)` guard in `HandleFilesSelected` is belt-and-suspenders, not load-bearing. ✅

### I-check 2: `ISubmissionService` implementors ✅

One concrete implementation: `SubmissionService.cs`. No test projects, no mock classes. Adding `AddFileToSubmissionAsync` to the interface is non-breaking in this codebase. ✅

---

## Nitpicks

**N1:** Sort order calculation — minor semantic collision risk.

`var sortOrder = _uploadedFiles.Count(f => !_filesToDelete.Contains(f.Id)) + _pendingFiles.Count`

If the user has 5 existing files, marks 2 for deletion (sort orders 0, 1), then uploads a new file: the new file gets sort order 3. But sort order 3 is already held by a non-deleted file. No DB exception (no unique constraint), but `SortOrder` semantics become inconsistent. Other views ordering by `SortOrder` may present files out of intended order.

Not blocking — `SortOrder` without a unique constraint is advisory, and the display in the wizard itself uses `_uploadedFiles` insertion order, not sort order. Low-priority cleanup for a future cycle.

**N2:** `_pendingFiles.Count` is always 0 in the sort order calculation during resume uploads (because `_pendingFiles.Clear()` is called before the loop). Build report correctly notes this. The expression could be simplified to `_uploadedFiles.Count(f => !_filesToDelete.Contains(f.Id))` but the current form is not incorrect.

**N3:** The commit `795bd15` bundles pre-existing FAIT pipeline reports in a NEXUS fix commit. Minor commit hygiene — future commits should keep service-specific pipeline artifacts in their own commits.

---

## Positive Observations

- `AddFileToSubmissionAsync` is clean and minimal. Follows existing `CreateAsync` pattern exactly. No surprises.
- Error handling per-file in the upload loop is correct: one file failing does not abort the loop. Other files still upload.
- `_pendingFiles.Clear()` before the loop in resume mode is correct — prevents the new-submission else-branch from having stale data, and correctly zeroes out `_pendingFiles.Count` for sort order calculation.
- `_isResume = true` is only set after successful init — the auth guard and not-found guard both return early, preventing half-initialized state.
- `_hasChanges` detection is correct for all cases including upload-then-immediately-delete edge case.
- Tony's own flagged edge case (the dual rendering in new-submission mode) was correctly identified in the build report and is accurate.

---

## What to Fix (NEEDS-CHANGES)

### Fix 1 — `HandleFilesSelected` race guard (5 min)

In `NewSpecWizard.razor`, in `HandleFilesSelected`, resume branch:

Move `_isUploading = true` to be the first statement **before** any `await`. Add a guard to return early if already uploading:

```csharp
private async Task HandleFilesSelected(IReadOnlyList<IBrowserFile> files)
{
    if (_isResume && _submissionId.HasValue)
    {
        if (_isUploading) return;          // ← ADD THIS
        _isUploading = true;               // ← MOVE UP (before any await)
        StateHasChanged();

        var upn = await UserContextService.GetUpnAsync();   // ← now after guard

        _pendingFiles.Clear();
        _fileErrors.Clear();
        // ... rest of loop unchanged
```

### Fix 2 — Suppress dual rendering in new-submission mode (10 min)

Option B (recommended): Re-introduce conditional `InitialFiles` on FileUploadZone and remove the `_pendingFiles` loop from the unified section:

```diff
 // In the unified list — remove _pendingFiles loop:
-    @foreach (var pendingFile in _pendingFiles)
-    {
-        var capturedPending = pendingFile;
-        <MudListItem T="string">
-            <div class="nexus-existing-file-item">
-                <MudText Typo="Typo.body2">@capturedPending.Name</MudText>
-            </div>
-        </MudListItem>
-    }

 // On FileUploadZone — restore conditional InitialFiles for new-submission mode:
-<FileUploadZone OnFilesSelected="HandleFilesSelected" />
+<FileUploadZone OnFilesSelected="HandleFilesSelected" InitialFiles="@(_isResume ? null : _pendingFiles.AsReadOnly())" />
```

This restores FileUploadZone as the display owner for new-submission pending files, and reserves the unified section exclusively for resume-mode existing files.

---

## CC Analysis Credit

This review was conducted using Claude Code CLI adversarial review. CC read all three changed files and FileUploadZone.razor in full, traced the Blazor Server event model, and identified both issues independently. CC findings were confirmed against the actual diff — no false positives dismissed.

---

# Review Report — NEXUS #1707 — Cycle 2 (Targeted)
**Reviewer:** Hawkeye | **Cycle:** 2 | **Date:** 2026-04-13
**Commits reviewed:** `e39851c` (fix), `7743e26` (docs)

---

## Verdict: PASS

All Cycle 1 issues (C1 race window, C2 dual render) are correctly fixed. Six targeted checks pass. Code is ready to ship.

---

## Targeted Review Results

### CHECK 1: C1 Guard Order ✅ PASS

`HandleFilesSelected` lines 488–492:

```csharp
private async Task HandleFilesSelected(IReadOnlyList<IBrowserFile> files)
{
    if (_isUploading) return;   // ← guard against concurrent invocations
    _isUploading = true;        // ← before any await
    try
    {
        if (_isResume && _submissionId.HasValue)
        { ...
```

Guard is the **first statement**. `_isUploading = true` is the **second statement**. No `await` between them. Both resume path and new-submission path are inside the `try` block.

### CHECK 2: try/finally Coverage ✅ PASS

`_isUploading = false` appears **only** in the `finally` block (line 530). Absent from `try` body. Both code paths (async resume upload loop + sync new-submission `_pendingFiles = files.ToList()`) covered by a single `finally`. Old in-try assignment removed.

### CHECK 3: @if (_isResume) Gate ✅ PASS

Outer gate (line 90):
```razor
@if (visibleExisting.Count > 0 || (_isResume && _pendingFiles.Count > 0))
```

Inner loop (lines 109–120):
```razor
@if (_isResume)
{
    @foreach (var pendingFile in _pendingFiles)
    { ... }
}
```

New-submission mode (`_isResume = false`): loop does not execute. Resume mode (`_isResume = true`): loop executes. Both gates verified.

### CHECK 4: InitialFiles Ternary ✅ PASS

Lines 124–125:
```razor
<FileUploadZone OnFilesSelected="HandleFilesSelected"
                InitialFiles="@(_isResume ? null : _pendingFiles.AsReadOnly())" />
```

Exact match. New-submission → passes `_pendingFiles.AsReadOnly()` to FileUploadZone (renders internally). Resume → passes `null` (FileUploadZone renders nothing; files show in unified list via `_uploadedFiles`).

### CHECK 5: Scope Creep ✅ PASS

- `e39851c`: 1 file changed — `NewSpecWizard.razor` only
- `7743e26`: 1 file changed — `pipeline/1707-BUILD-REPORT.md` only

No additional files modified.

### CHECK 6: Build ✅ PASS

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Summary

Both C1 (race window) and C2 (dual render) fixes are correctly implemented. The concurrent-invocation guard is properly ordered (check-then-set before any `await`), the `try/finally` provides unconditional reset, markup gating routes file display through the correct owner per mode, and the build is clean. No regressions, no scope creep.

**PASS — cleared for next pipeline stage.**
