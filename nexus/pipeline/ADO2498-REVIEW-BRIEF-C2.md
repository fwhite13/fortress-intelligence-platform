# REVIEW Assignment: ADO#2498 — Cycle 2

## Task
**Integrate IWiClassifier into ArtifactGenerationService — Re-review after cycle 1 fixes**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2498
**Review cycle:** 2 of 2

## Spec (MANDATORY — read before reviewing)
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Relevant sections: §4 (data model), §5 (component map), §6 (service layer).

## What changed in cycle 2 (commit `a965b58`)

| File | Change |
|------|--------|
| `Models/Entities/WorkItemRecord.cs` | Added `public string? ParentTitle { get; set; }` |
| `Data/NexusDbContext.cs` | Added EF column mapping: `parent_title VARCHAR(500) NULL` |
| `Migrations/20260428131416_AddWorkItemRecordParentTitle.cs` | New migration — adds `parent_title` to `work_item_records` |
| `Migrations/20260428131416_AddWorkItemRecordParentTitle.Designer.cs` | Auto-generated designer |
| `Migrations/NexusDbContextModelSnapshot.cs` | Updated snapshot |
| `Models/DTOs/AdoWorkItemDto.cs` | Added `public List<string>? PredecessorTitles { get; set; }` |
| `Services/StubAdoService.cs` | Added `ParentTitle` and `PredecessorTitles` mapping in BOTH `CreateWorkItemAsync` and `CreateWorkItemBatchAsync` |

## Build result
SUCCEEDED — 0 errors, 1 pre-existing warning.

## Cycle 2 focus: verify the two fixes are correct

### C1 fix verification — ParentTitle
1. `WorkItemRecord.ParentTitle` property exists as `string?` ✅ (check)
2. NexusDbContext column mapping configured correctly — `parent_title`, nullable, VARCHAR(500)? (check actual column type used)
3. Migration `AddWorkItemRecordParentTitle` Up() adds `parent_title` to `work_item_records`, Down() drops it
4. StubAdoService: `record.ParentTitle = dto.ParentTitle` present in **both** `CreateWorkItemAsync` AND `CreateWorkItemBatchAsync`

### C2 fix verification — PredecessorTitles
1. `AdoWorkItemDto.PredecessorTitles` property exists as `List<string>?`
2. StubAdoService: `record.PredecessorTitles = dto.PredecessorTitles` present in **both** methods
3. Tony noted AI response JSON auto-deserializes via `PropertyNameCaseInsensitive = true` in `ArtifactGenerationService.ParseWorkItems` — verify `predecessorTitles` field actually exists in the AI response DTO/model and flows through to `AdoWorkItemDto`

### Quick re-check of cycle 1 pass items
Spot-check that the cycle 1 passing items weren't accidentally broken by the cycle 2 changes:
- `TestedByTitles` still mapped in StubAdoService
- `ExternalDependencyCount` still set before save
- Classification fields (`WiTemplate`, `IsExternalDependency`, `ExternalOwner`) still mapped

## MANDATORY: Use Claude Code CLI
```
cat /tmp/review-2498-c2-brief.txt | claude --model sonnet --print --dangerously-skip-permissions
```
Review Report MUST include CC invocation. Do not reason about code without CC reading it first.

## Deliverables
1. **Review Report** at `/home/fredw/projects/fip/nexus/pipeline/ADO2498-REVIEW-REPORT-C2.md`
   - Verdict: PASS / NEEDS-CHANGES / FAIL
   - Explicit confirmation both C1 and C2 fixes are correct
   - CC invocation used
2. **ADO comment** on WI #2498:
   ```
   mcporter call devops.add_comment project="FAIT" id=2498 text="**[Hawkeye — REVIEW cycle 2]**
   Code review [PASS/NEEDS-CHANGES]. [summary]"
   ```

## When done
```
openclaw system event --text "ADO2498 REVIEW C2 COMPLETE: [PASS/NEEDS-CHANGES]" --mode now
```
