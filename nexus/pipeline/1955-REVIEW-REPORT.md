# Review Report: ADO#1955 — NEXUS Vision Timeout + Image Description + Discovery Render Bug

**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 1
**Commit Reviewed:** `8bf2dc6`
**Verdict:** PASS

---

## Spec Compliance Check

### Files Changed (actual vs expected)

| File | Expected | Status |
|------|----------|--------|
| `appsettings.json` | ✅ expected | ✅ modified |
| `UploadedFile.cs` | ✅ expected | ✅ modified |
| `NexusDbContext.cs` | ✅ expected | ✅ modified |
| `20260415221112_AddUploadedFileUserDescription.cs` | ✅ expected | ✅ created |
| `20260415221112_AddUploadedFileUserDescription.Designer.cs` | (EF generated) | ✅ created |
| `NexusDbContextModelSnapshot.cs` | ✅ expected | ✅ updated |
| `NewSpecWizard.razor` | ✅ expected | ✅ modified |
| `SpecGenerationService.cs` | ✅ expected | ✅ modified |
| `DiscoveryService.cs` | ✅ expected | ✅ modified |
| `ISubmissionService.cs` | not pre-listed | ✅ required for UpdateUploadedFileAsync |
| `SubmissionService.cs` | not pre-listed | ✅ required for UpdateUploadedFileAsync |

`ISubmissionService.cs` and `SubmissionService.cs` were not in the pre-listed files but are required additions to support `UpdateUploadedFileAsync`. In-scope and correct.

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

All cross-file contracts verified:

- `UploadedFile.UserDescription (string?)` → `NexusDbContext` mapping → `Migration AddColumn (nullable: true)` → in sync ✅
- `ISubmissionService.UpdateUploadedFileAsync` ↔ `SubmissionService.UpdateUploadedFileAsync` ↔ caller in `NewSpecWizard.razor` → all present and matching ✅
- `_descriptionChanged` flag: set in `ValueChanged` handler, consumed in `GoToStep2Discovery()` and `_hasContentChanges` computed property → consistent ✅
- Vision prompt conditional (`!string.IsNullOrWhiteSpace(file.UserDescription)`) appears in both `SpecGenerationService.cs` and `DiscoveryService.cs` → ✅ both covered
- `VisionMaxTokens=2000`, `TimeoutSeconds=300` in `appsettings.json` → both consumed via `_specGenConfig` in `SpecGenerationService` ✅ (DiscoveryService timeout uses `_specGenConfig.TimeoutSeconds`, VisionMaxTokens is pre-existing hardcoded `2000` — pre-existing, out of scope)

---

## CC Review Summary

CC Sonnet reviewed all 14 changed files with 15 explicit checklist items. No critical or important findings. CC confirmed:
- All spec compliance points met
- Migration is clean (AddColumn only, no destructive ops)
- `_descriptionChanged` flag correctly gates `_hasContentChanges` computed property
- `StateHasChanged()` correctly placed in `else` branch outside `Task.Run`
- Entity tracking and DB write safety confirmed for Blazor Server scoped context
- One nitpick: loop inefficiency

---

## Critical Issues — 0

None.

---

## Important Issues — 0

None.

---

## Nitpicks — 1

#### N1: `UpdateUploadedFileAsync` loop iterates all files, not just image files

- **File:** `NewSpecWizard.razor` (~line 470)
- **Issue:** When `_descriptionChanged` is true, `GoToStep2Discovery()` calls `UpdateUploadedFileAsync` for every file in `_uploadedFiles`, including PDFs, HTML, and other non-image files whose `UserDescription` is always null. EF's `Update()` marks all properties Modified and issues a full `UPDATE` SQL statement per row even for unchanged non-image files.
- **Impact:** Cosmetic — extra DB roundtrips proportional to non-image file count. No data corruption risk (values written = values read). Typical file count is 0-10, so negligible in practice.
- **Fix (optional):**
  ```diff
  - foreach (var uploadedFile in _uploadedFiles)
  + foreach (var uploadedFile in _uploadedFiles.Where(f => f.FileType == FileType.Image))
  ```
- **Blocking:** No. Not blocking.

---

## Positive Observations

- Migration is surgically clean — `AddColumn` only, correct `Down()`, correct `utf8mb4` charset annotation.
- `DbContext` mapping correctly omits `IsRequired()` and `HasColumnType()` — follows Pomelo conventions properly.
- `_descriptionChanged` flag is a clean approach — not reusing `_hasChanges` (which is computed, not assignable) and not polluting `_hasContentChanges` logic.
- Vision prompt conditional is identical in structure across both `SpecGenerationService` and `DiscoveryService` — good consistency.
- `StateHasChanged()` fix is minimal and correctly placed in the `else` branch outside `Task.Run`.
- `UpdateUploadedFileAsync` is properly gated with `if (_descriptionChanged)` so it only runs when needed.

---

## Acceptance Criteria Verification

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `VisionMaxTokens=2000`, `TimeoutSeconds=300` in appsettings | ✅ Verified — diff confirmed |
| 2 | `UploadedFile.UserDescription` property (`string?`) | ✅ Verified — `UploadedFile.cs:17` |
| 3 | DbContext mapping `HasColumnName` + `HasMaxLength(500)` | ✅ Verified — `NexusDbContext.cs:40` |
| 4 | Migration `AddColumn` only — no destructive ops | ✅ Verified — migration file reviewed |
| 5 | Image files in Step 1 show `MudTextField` | ✅ Verified — gated by `FileType.Image` check |
| 6 | Description persisted via `UpdateUploadedFileAsync` on step forward | ✅ Verified — call chain confirmed through to `SaveChangesAsync()` |
| 7 | `SpecGenerationService` uses `UserDescription` in vision prompt | ✅ Verified — conditional present |
| 8 | `DiscoveryService` uses `UserDescription` in vision prompt | ✅ Verified — conditional present |
| 9 | Discovery `else` branch calls `StateHasChanged()` | ✅ Verified — correct placement, outside `Task.Run` |
| 10 | `dotnet build` → 0 errors | ✅ Verified — build succeeded, 0 warnings, 0 errors |

---

## Verdict: PASS ✅

All 10 acceptance criteria met. No critical or important issues. One nitpick (loop scope) is safe to ship as-is. Migration is clean and non-destructive.
