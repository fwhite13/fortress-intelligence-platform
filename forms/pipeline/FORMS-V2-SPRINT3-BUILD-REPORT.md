# FORMS v2 Sprint 3 — Build Report
**Date:** 2026-03-03
**Branch:** main
**Commit:** 7a2ae3f

---

## Build Status: ✅ SUCCESS — 0 errors

```
Build succeeded.
    0 Error(s)
    121 Warning(s)  ← pre-existing NuGet version warnings, no new warnings
```

---

## What Was Built

### 1. `FormFieldCode` Entity — `FortressFormTools.Data/Entities/FormFieldCode.cs`
- New entity: one record per unified field code per project
- Fields: `FieldCode`, `FieldLabel`, `FieldType`, `IsSensitive`, `IsShared`, `IsRequired`, `PanelId`, `CarrierSources` (JSON array), `SectionName`, `SortOrder`, `ProjectId` FK
- `[Table("FormFieldCodes")]` attribute used instead of `.ToTable()` — required because `FortressFormTools.Data.csproj` only references `Microsoft.EntityFrameworkCore` base package (no Relational extension)

### 2. `AppDbContext` — `FortressFormTools.Data/AppDbContext.cs`
- Added `DbSet<FormFieldCode> FormFieldCodes`
- Added model config: unique index on `(ProjectId, FieldCode)`, cascade delete from `FormProject`

### 3. `Program.cs` — DB Init
- Added `CREATE TABLE IF NOT EXISTS FormFieldCodes` block, placed after `form_projects` and before `alterStatements` loop
- Idempotent; safe on existing deployments

### 4. `CrossReferenceService.cs` — Extended
**New method:** `CrossReferenceProjectAsync(int projectId, CancellationToken ct)`
- Loads all approved `FormLibrary` records (with `FormField` children) for the project
- Builds a structured Bedrock/Claude prompt summarizing extracted fields per carrier
- Calls Claude via existing `RunClaudeAsync` (same process-spawn pattern as `GeneratorService`)
- Parses JSON array response into `FormFieldCode` records and persists (delete+re-insert for idempotency)
- Calls `UpsertProjectQuestionSetAsync` — creates or refreshes a `QuestionSet` with the unified fields
- Silent-fail: any exception returns `CrossReferenceResult` with `ErrorMessage`, never throws

**New result type:** `CrossReferenceResult` record (Sprint 3)
- `(int ProjectId, int FieldsFound, int SharedFields, int CarrierSpecificFields, List<string> PanelsDetected, string? ErrorMessage)`

**Legacy rename:** `CrossReferenceResult` → `FieldCrossReferenceResult` (Sprint 1/2 question-set level)
- Controller and callers use `Ok(result)` so rename is transparent to JSON responses

**New internal DTO:** `FieldCodeDto` for parsing Bedrock JSON response

### 5. `ProjectCrossReference.razor` — New Page
**Route:** `/projects/{ProjectId:int}/cross-reference`
- Header with back button, project name, vertical chip
- Alert: shows approved doc count + carrier names; warns if none approved
- "Run Cross-Reference" button (disabled when no approved docs or running)
- Progress indicator during analysis (15–45 sec estimate)
- Error display if analysis fails
- Results section: summary chips (total/shared/carrier-specific/panels)
- Results table: FieldCode (monospace gold), Label, Type, Shared chip, Section, Carriers, Flags (sensitive/required/panel icons)
- "View Question Set →" button navigates to generated question set after successful run
- Pre-loads existing field codes from DB on init (persisted across sessions)

**Note:** Used `List<FortressFormTools.Data.Entities.FormLibrary>` (fully qualified) because `FormLibrary.razor` exists in the same namespace and shadows the entity type

### 6. `ProjectDetail.razor` — Updated
- Added "Run Cross-Reference →" button on the Documents tab, visible only when project has ≥1 approved document
- Links to `/projects/{id}/cross-reference`
- Sits alongside "Extract All Pending" button in a `MudStack Row`

---

## Architecture Notes

### Extraction data storage
The Sprint 3 prompt uses `FormField` records (one row per extracted field per form) loaded via `Include(f => f.Fields)`. There is no separate `ExtractionJson` blob on `FormLibrary`. The prompt builds a per-carrier field list from `FormField.FieldLabel`, `FieldType`, `IsRequired`, `SectionName`.

### Claude CLI vs Bedrock SDK
The existing `RunClaudeAsync` uses `Process.Start("claude", "--model sonnet -p")` — a CLI pipe. Sprint 3 uses the same pattern, consistent with `GeneratorService`. Model arg is "sonnet" (resolves to claude-sonnet-4-6 via the CLI's configured model alias).

### CC CLI status
CC CLI was rate-limited/unavailable at build time. Implementation done via direct Bedrock (inline). Noted for spillover tracking.

---

## Files Changed
| File | Change |
|------|--------|
| `FortressFormTools.Data/Entities/FormFieldCode.cs` | **New** — unified field code entity |
| `FortressFormTools.Data/AppDbContext.cs` | Added `FormFieldCodes` DbSet + model config |
| `FortressFormTools.Web/Program.cs` | Added `FormFieldCodes` CREATE TABLE block |
| `FortressFormTools.Web/Services/CrossReferenceService.cs` | Added Sprint 3 analysis logic; renamed legacy result type |
| `FortressFormTools.Web/Components/Pages/ProjectCrossReference.razor` | **New** — project-scoped cross-reference UI |
| `FortressFormTools.Web/Components/Pages/ProjectDetail.razor` | Added "Run Cross-Reference →" button |

---

## What Was Preserved
- `/cross-reference` v1 page (`CrossReference.razor`) — **untouched**
- `AnalyzeFormsAsync` / question-set cross-reference flow — **preserved**, return type renamed to `FieldCrossReferenceResult` (JSON output unchanged)
- `SaveBulkFieldsAsync`, all legacy DTOs — **preserved**

---

## Git
```
commit 7a2ae3f
feat(v2-s3): cross-reference engine — FormFieldCode entity, Bedrock analysis, ProjectCrossReference UI
```
