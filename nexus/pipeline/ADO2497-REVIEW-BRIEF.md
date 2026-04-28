# REVIEW Assignment: ADO#2497

## Task
**Add new fields to WorkItemRecord and ArtifactSet models**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2497
**Review cycle:** 1 of 2

## Spec (MANDATORY — read before reviewing)
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Focus on **§4 — Data Model Changes** for exact column specs and SQL DDL.

## Files Modified by Tony (commit `f527f50`)

| File | Action |
|------|--------|
| `src/FortressNexus.Web/Models/Entities/WorkItemRecord.cs` | Modified — 5 new fields added |
| `src/FortressNexus.Web/Models/Entities/ArtifactSet.cs` | Modified — ExternalDependencyCount added |
| `src/FortressNexus.Web/Data/NexusDbContext.cs` | Modified — column mappings, JSON conversions |
| `src/FortressNexus.Web/Migrations/20260428030456_AddDecompositionUpgradeFields_20260427.cs` | Created — EF Core migration |
| `src/FortressNexus.Web/Migrations/20260428030456_AddDecompositionUpgradeFields_20260427.Designer.cs` | Created — auto-generated |
| `src/FortressNexus.Web/Migrations/NexusDbContextModelSnapshot.cs` | Updated |

## Build Report Summary
- Build: SUCCEEDED (0 errors, 1 pre-existing warning)
- Commit: `f527f50`
- FK check: no FK constraints reference work_item_records — no guard needed (verified)
- Migration: generated, not yet applied to dev DB (no local MySQL available — will apply at deploy time)
- WiTemplateType: referenced from IWiClassifier.cs, not redefined ✅

## ⚠️ Flag to Investigate: wi_template column type

Spec §4 specifies `wi_template` as:
```sql
ENUM('standard','infrastructure','migration','test-case') NOT NULL DEFAULT 'standard'
```

Tony stored it as `VARCHAR(20)` (with string conversion from `WiTemplateType` enum) — consistent with the existing `wi_type` VARCHAR(50) pattern in this codebase, which does NOT use MySQL ENUM types.

**Your job:** Read the migration file and the spec. Make a call:
- If `VARCHAR(20)` with string conversion is functionally equivalent and consistent with the codebase pattern → PASS with a note
- If the spec's ENUM constraint matters for data integrity (rejects invalid values at DB level) → flag as Important

Either answer is acceptable. Just make the call explicitly.

## Review Focus

### 1. WorkItemRecord.cs — field correctness
- `PredecessorTitles`: `List<string>?`, JSON-serialized?
- `IsExternalDependency`: `bool`, default `false`?
- `ExternalOwner`: `string?`?
- `WiTemplate`: `WiTemplateType` type (from IWiClassifier.cs), not redefined locally?
- `TestedByTitles`: `List<string>?`, JSON-serialized?
- `WiType`: does it handle "Test Case" as a valid value?

### 2. ArtifactSet.cs
- `ExternalDependencyCount`: `int`, default `0`?

### 3. NexusDbContext.cs — EF configuration
- Are JSON conversions for `PredecessorTitles` and `TestedByTitles` using `HasConversion` + `ValueComparer`? (Required for EF change tracking to detect list mutations correctly)
- Is `WiTemplate` mapped with a string conversion to/from `WiTemplateType`?
- Column name mappings correct? (`predecessor_titles`, `is_external_dependency`, `external_owner`, `wi_template`, `tested_by_titles`, `external_dependency_count`)

### 4. Migration file — Up/Down correctness
Read `Migrations/20260428030456_AddDecompositionUpgradeFields_20260427.cs` carefully:
- Does `Up()` add all 6 new columns (5 on work_item_records + 1 on artifact_sets)?
- Does `Down()` drop them cleanly?
- Are default values correct? (`is_external_dependency` default 0, `wi_template` default 'Standard', `external_dependency_count` default 0)
- Is `predecessor_titles` nullable? `external_owner` nullable? `tested_by_titles` nullable?

### 5. No WiTemplateType redefinition
Confirm `WiTemplateType` is NOT redefined in the Models layer — it must be referenced only from `Services/IWiClassifier.cs`.

### 6. Migration not yet applied — note for deploy
Tony could not apply the migration (no local MySQL). Flag this explicitly in your report so Rhodey knows to run `dotnet ef database update` as part of the deploy. This is not a blocker for PASS — just a deploy note.

## MANDATORY: Use Claude Code CLI
Write your review brief to a temp file, then:
```
cat /tmp/review-2497-brief.txt | claude --model sonnet --print --dangerously-skip-permissions
```
Your Review Report MUST include the CC invocation used.

## Deliverables
1. **Review Report** at `/home/fredw/projects/fip/nexus/pipeline/ADO2497-REVIEW-REPORT.md`
   - Verdict: PASS / NEEDS-CHANGES / FAIL
   - Explicit call on the VARCHAR vs ENUM question
   - Migration deploy note for Rhodey
   - CC invocation used
2. **ADO comment** on WI #2497:
   ```
   mcporter call devops.add_comment project="FAIT" id=2497 text="**[Hawkeye — REVIEW cycle 1]**
   Code review [PASS/NEEDS-CHANGES]. [summary]"
   ```

## When done
```
openclaw system event --text "ADO2497 REVIEW COMPLETE: [PASS/NEEDS-CHANGES]" --mode now
```
