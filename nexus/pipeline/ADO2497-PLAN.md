# BUILD Assignment: ADO#2497

## Task
**Add new fields to WorkItemRecord and ArtifactSet models**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2497

## MANDATORY: Read the spec first
Read the full spec before starting ANY code changes:
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Focus on **§4 — Data Model Changes** — it has the exact SQL DDL for every column, EF Core model notes, and the migration name.

## Repo
`/home/fredw/projects/fip/nexus/`
Working directory: `src/FortressNexus.Web/`

## Context
ADO#2490 (IWiClassifier + WiClassifierService) is already deployed. `WiTemplateType` enum is defined in `Services/IWiClassifier.cs`. Do NOT redefine it — just reference it from the models.

## What to build

### 1. Update `Models/WorkItemRecord.cs`

Add the following properties. Check how existing JSON-serialized list properties are handled in this model (look for existing patterns using `HasConversion` or `[Column(TypeName="json")]`) and follow the same pattern for `PredecessorTitles` and `TestedByTitles`.

New fields:
```csharp
// Extend WiType to include "Test Case" — check the existing enum or string mapping and add it
// (may be a C# enum or a string constant — match existing pattern)

// Predecessor links — JSON-serialized array of WI title strings
public List<string>? PredecessorTitles { get; set; }

// External dependency fields
public bool IsExternalDependency { get; set; } = false;
public string? ExternalOwner { get; set; }

// WI template classification — references WiTemplateType from IWiClassifier.cs
public WiTemplateType WiTemplate { get; set; } = WiTemplateType.Standard;

// Test Case relationship — JSON-serialized array of Test Case WI titles
public List<string>? TestedByTitles { get; set; }
```

DB column specs (for migration):
- `predecessor_titles` — JSON NULL
- `is_external_dependency` — TINYINT(1) NOT NULL DEFAULT 0
- `external_owner` — VARCHAR(100) NULL
- `wi_template` — ENUM('standard','infrastructure','migration','test-case') NOT NULL DEFAULT 'standard'
- `tested_by_titles` — JSON NULL

Also extend `wi_type` ENUM to include `'Test Case'` — see SQL in spec §4.

**⚠️ MYSQL FK WARNING:** Before generating the migration that alters `wi_type`, check if any FK constraints reference `work_item_records`. If so, wrap the `AlterColumn` with `DropForeignKey` before and `AddForeignKey` after. This has burned us twice before — check first, migrate second.

To check for FKs:
```sql
SELECT CONSTRAINT_NAME, TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
WHERE REFERENCED_TABLE_NAME = 'work_item_records'
  AND TABLE_SCHEMA = DATABASE();
```
Run this against the dev DB before writing the migration.

### 2. Update `Models/ArtifactSet.cs`

Add:
```csharp
public int ExternalDependencyCount { get; set; } = 0;
```

DB: `external_dependency_count INT NOT NULL DEFAULT 0`

### 3. Create EF Core migration `AddDecompositionUpgradeFields_20260427`

Migration file: `Migrations/AddDecompositionUpgradeFields_20260427.cs`

The migration must cover exactly:
1. `wi_type` ENUM update to add `'Test Case'` (with FK guard if needed)
2. Five new columns on `work_item_records`: `predecessor_titles`, `is_external_dependency`, `external_owner`, `wi_template`, `tested_by_titles`
3. One new column on `artifact_sets`: `external_dependency_count`

Generate with: `dotnet ef migrations add AddDecompositionUpgradeFields_20260427`
Then verify the generated Up/Down methods match the spec §4 SQL exactly. Adjust if EF generates anything wrong.

Apply migration against dev DB: `dotnet ef database update`
Confirm it applies with no errors.

## ADO Updates (MANDATORY)
After implementing, add a comment to ADO WI #2497:
```
mcporter call devops.add_comment project="FAIT" id=2497 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {summary}. Build: SUCCEEDED. Migration applied: CLEAN."
```

## Build Report required
Create `/home/fredw/projects/fip/nexus/pipeline/ADO2497-BUILD-REPORT.md` with:
- Files created/modified (with full paths)
- Commit hash
- Build result (`dotnet build` output)
- Migration apply result (`dotnet ef database update` output)
- CC invocation command used
- Self-review checklist: all AC items verified

## Notify Maria when done
When completely finished, run:
openclaw system event --text "ADO2497 BUILD COMPLETE: WorkItemRecord + ArtifactSet model fields + migration" --mode now
