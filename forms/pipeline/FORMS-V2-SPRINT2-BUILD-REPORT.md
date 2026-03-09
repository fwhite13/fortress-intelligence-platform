# FORMS v2 Sprint 2 — Build Report

**Date:** 2026-03-03  
**Branch:** main  
**Commit:** bbd0b63  
**Build Status:** ✅ 0 errors, 12 warnings (pre-existing package resolution warnings, not Sprint 2 related)

---

## What Was Built

### 1. `FormLibrary` Entity — New Fields
**`FortressFormTools.Data/Entities/FormLibrary.cs`**
- Added `DocumentType` (`string?`, `MaxLength(50)`) — values: `application`, `supplement`, `pilot_form`, `driver_schedule`, `vehicle_schedule`, `other`
- Added `ApprovedAt` (`DateTime?`) — set when a document is approved

**`FortressFormTools.Web/Program.cs`**
- Added two idempotent ALTER TABLE statements in the existing try-catch-1060 block:
  - `ALTER TABLE FormLibraries ADD COLUMN DocumentType VARCHAR(50) NULL`
  - `ALTER TABLE FormLibraries ADD COLUMN ApprovedAt DATETIME(6) NULL`

### 2. `ProjectDetail.razor` — Documents Tab Enhancement
- **New table columns:** File Name (link), Carrier, Document Type (inline MudSelect), Extraction Status (chip), Approved (green chip when `ApprovedAt != null`), Actions
- **Document Type selector:** Inline MudSelect per row; saves immediately via `IDbContextFactory` on change (`SaveDocumentType`)
- **Approve button:** Shown when `ApprovedAt == null && Status in [Draft, Reviewed, Approved]`; sets `ApprovedAt = UtcNow` and `Status = "Approved"` (`ApproveDocument`)
- **Auto project status:** After each approval, checks if all docs approved → sets project status to `"extracted"`
- **Delete button:** Per-row delete via `IDialogService.ShowMessageBox` confirm → `DELETE /api/forms/{id}`
- **Upload DocumentType:** Added `_uploadDocumentType` state + MudSelect in upload zone; passed as `documentType` form field on upload
- **Navigation links:** All View/Review hrefs include `?projectId={ProjectId}` for back-button context
- **`ExtractAllPending` button:** Was already present from Sprint 1

### 3. `FormDetail.razor` — Project Context
- Added `[SupplyParameterFromQuery] public int? ProjectId { get; set; }`
- Back button (both "form not found" and top bar): conditionally renders "Back to Project" → `/projects/{ProjectId}` or "Back to Library" → `/forms`
- `Nav.NavigateTo()` in `DeleteForm()` also respects `ProjectId`

### 4. `FormReview.razor` — Project Context
- Added `[SupplyParameterFromQuery] public int? ProjectId { get; set; }`
- Back arrow `MudIconButton` href/title conditionally updated with project context

---

## Files Modified
| File | Change |
|------|--------|
| `FortressFormTools.Data/Entities/FormLibrary.cs` | +`DocumentType`, +`ApprovedAt` |
| `FortressFormTools.Web/Program.cs` | +2 ALTER TABLE statements |
| `FortressFormTools.Web/Components/Pages/ProjectDetail.razor` | New table columns, DocType selector, Approve/Delete actions, upload DocType, new methods |
| `FortressFormTools.Web/Components/Pages/FormDetail.razor` | `[SupplyParameterFromQuery] ProjectId`, updated back button |
| `FortressFormTools.Web/Components/Pages/FormReview.razor` | `[SupplyParameterFromQuery] ProjectId`, updated back button |

---

## Technical Notes
- **CC CLI:** Attempted pipe mode; process timed out after partial completion. Final file edits done in-context + manual fixup (duplicate field removal in FormLibrary.cs from CC partial write).
- **Patterns used:** `IDbContextFactory<AppDbContext>` for all DB ops, `IDialogService.ShowMessageBox` for confirms, `StateHasChanged()` after mutations, no navigation properties in WHERE clauses.
- **FormProject.UpdatedAt:** Not set in `ApproveDocument` — property not present on `FormProject` entity (only `Status` updated).
- **Pre-existing warnings:** NU1504 (duplicate PackageReference) and NU1603 (PdfPig version resolution) are not Sprint 2 related.

---

## Ready
Pushed to `origin/main` — commit `bbd0b63`.
