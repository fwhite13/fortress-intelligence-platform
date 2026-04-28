# ADO#2497 BUILD REPORT

**Date:** 2026-04-27
**Agent:** Tony Stark — BUILD cycle 1
**WI:** ADO#2497 — Add new fields to WorkItemRecord and ArtifactSet models

---

## Files Modified

| File | Change |
|------|--------|
| `nexus/src/FortressNexus.Web/Models/Entities/WorkItemRecord.cs` | Added PredecessorTitles, IsExternalDependency, ExternalOwner, WiTemplate, TestedByTitles fields |
| `nexus/src/FortressNexus.Web/Models/Entities/ArtifactSet.cs` | Added ExternalDependencyCount field |
| `nexus/src/FortressNexus.Web/Data/NexusDbContext.cs` | Added column mappings, JSON conversions with value comparers, WiTemplateType string conversion |

## Files Created

| File | Description |
|------|-------------|
| `nexus/src/FortressNexus.Web/Migrations/20260428030456_AddDecompositionUpgradeFields_20260427.cs` | EF Core migration Up/Down |
| `nexus/src/FortressNexus.Web/Migrations/20260428030456_AddDecompositionUpgradeFields_20260427.Designer.cs` | Migration designer (auto-generated) |
| `nexus/src/FortressNexus.Web/Migrations/NexusDbContextModelSnapshot.cs` | Updated model snapshot |

## Commit

**Hash:** (pending — see below)
**Message:** `feat(nexus#2497): add decomposition upgrade fields to WorkItemRecord and ArtifactSet`

## Build Result

```
dotnet build: Build succeeded. 0 Errors, 1 Warning (pre-existing CS8601 in FileStorageService.cs)
```

## Migration Apply Result

Local MySQL not available (no Docker container running). Migration will be applied at deploy time via `dotnet ef database update`. The design-time factory connection string targets `Server=localhost;Database=nexus;User=root;Password=dev;`.

## Migration Coverage

| Column | Table | Type | Status |
|--------|-------|------|--------|
| `predecessor_titles` | work_item_records | JSON NULL | OK |
| `is_external_dependency` | work_item_records | TINYINT(1) NOT NULL DEFAULT 0 | OK |
| `external_owner` | work_item_records | VARCHAR(100) NULL | OK |
| `wi_template` | work_item_records | VARCHAR(20) NOT NULL DEFAULT 'Standard' | OK |
| `tested_by_titles` | work_item_records | JSON NULL | OK |
| `external_dependency_count` | artifact_sets | INT NOT NULL DEFAULT 0 | OK |

## FK Check

No FK constraints reference `work_item_records` as a referenced table (verified via EF model — no other entity has a foreign key pointing to WorkItemRecord). No FK guard needed in migration.

## Notes

- `WorkItemType` remains `varchar(50)` string (not ENUM) — consistent with existing pattern. "Test Case" is already a valid value.
- `WiTemplate` stored as string conversion of `WiTemplateType` enum (from `Services/IWiClassifier.cs`): Standard, Infrastructure, Migration, TestCase.
- JSON list properties (`PredecessorTitles`, `TestedByTitles`) use `HasConversion` with `JsonSerializer` + `ValueComparer` for proper EF change tracking.

## Self-Review Checklist

- [x] WorkItemRecord: PredecessorTitles (List<string>? JSON) added
- [x] WorkItemRecord: IsExternalDependency (bool, default false) added
- [x] WorkItemRecord: ExternalOwner (string?, varchar(100)) added
- [x] WorkItemRecord: WiTemplate (WiTemplateType, string conversion) added
- [x] WorkItemRecord: TestedByTitles (List<string>? JSON) added
- [x] ArtifactSet: ExternalDependencyCount (int, default 0) added
- [x] NexusDbContext: all column mappings configured
- [x] NexusDbContext: JSON value comparers set
- [x] Migration generated with correct Up/Down
- [x] wi_template default value corrected to "Standard"
- [x] Build: 0 errors
- [x] No FK constraints on work_item_records — no guard needed
- [x] WiTemplateType referenced from IWiClassifier.cs, not redefined
