# Build Report — ADO#2820 Cycle 2

**[Tony Stark — BUILD cycle 2]**
**Commit:** `c933e3b`
**Date:** 2026-05-06
**Build:** SUCCEEDED (0 errors, 1 pre-existing warning unrelated to this change)

---

## What was built

Two correctness fixes for the Nexus decomposition pipeline:
1. **I1:** `AdoWorkItemId` and `AdoWorkItemUrl` on `WorkItemRecord` made nullable — semantically correct since these fields are only populated post-ADO-creation
2. **I2:** Empty-DTO guard added in `DecomposeAndPersistAsync` — prevents phantom ArtifactSet creation with 0 records and incorrect status advancement

---

## Files changed

| File | Change |
|------|--------|
| `Models/Entities/WorkItemRecord.cs` | `int AdoWorkItemId` → `int?`; `string AdoWorkItemUrl = ""` → `string?` |
| `Data/NexusDbContext.cs` | Removed `.IsRequired()` from `ado_work_item_id` and `ado_work_item_url` column configs |
| `Components/Pages/NexusArtifacts.razor` | Removed `AdoWorkItemId = 0` and `AdoWorkItemUrl = ""` from `AddWi` initializer |
| `Controllers/NexusArtifactsController.cs` | Same removals in controller `AddWi` endpoint |
| `Services/ArtifactGenerationService.cs` | Added `dtos.Count == 0` guard throwing `InvalidOperationException` before ArtifactSet creation |
| `Services/StubAdoService.cs` | `.AdoWorkItemId` → `.AdoWorkItemId.GetValueOrDefault()` in `CreateWorkItemBatchAsync` title-map (build fix for Dictionary<string, int>) |
| `Migrations/20260506143015_MakeAdoWorkItemFieldsNullable.cs` | EF migration — alters both columns to nullable |
| `Migrations/20260506143015_MakeAdoWorkItemFieldsNullable.Designer.cs` | EF migration designer snapshot |
| `Migrations/NexusDbContextModelSnapshot.cs` | Updated model snapshot |

---

## Migration

**Name:** `MakeAdoWorkItemFieldsNullable`
**File:** `20260506143015_MakeAdoWorkItemFieldsNullable.cs`

Alters:
- `ado_work_item_id` — `int NOT NULL` → `int NULL`
- `ado_work_item_url` — `varchar(500) NOT NULL` → `varchar(500) NULL`

---

## CC sessions

1 CC session (Sonnet), sequential. Both fixes covered in a single brief.

---

## Acceptance criteria verification

- [x] **I1 entity** — `WorkItemRecord.AdoWorkItemId` is `int?`, `AdoWorkItemUrl` is `string?`
- [x] **I1 DbContext** — `.IsRequired()` removed from both column configs
- [x] **I1 migration** — `MakeAdoWorkItemFieldsNullable` generated, verified alters columns to nullable
- [x] **I1 guard scan** — No `AdoWorkItemId > 0` guards found; `AddWi` explicit-zero assignments removed from razor + controller
- [x] **I2 guard** — `dtos.Count == 0` throws `InvalidOperationException` before ArtifactSet is created
- [x] **Build** — 0 errors

---

## Things Clint should scrutinize

- `StubAdoService.CreateWorkItemBatchAsync` — the title-map was `Dictionary<string, int>` referencing `record.AdoWorkItemId`. CC used `.GetValueOrDefault()` as the build fix. This is correct for stub values (they're always assigned), but worth confirming intent.
- `WriteBackResultsAsync` in razor — still assigns `record.AdoWorkItemId = result.AdoWorkItemId` (now `int?` → `int?`) and `record.AdoWorkItemUrl = result.AdoWorkItemUrl` (now `string?` → `string?`). Assignment types now match nullable-to-nullable.
- `NexusArtifactsController.cs` — CC found and updated the controller's `AddWi` endpoint as well (parallel to razor change). Confirm both match.

---

## How to test locally

1. Run migration: `cd src/FortressNexus.Web && dotnet ef database update`
2. Verify `work_item_records.ado_work_item_id` and `ado_work_item_url` columns accept NULL
3. Trigger decomposition with a valid spec — confirm ArtifactSet is created with nullable ADO fields
4. Test empty-DTO guard: mock Bedrock to return empty array → expect `InvalidOperationException` in logs, no ArtifactSet row created
