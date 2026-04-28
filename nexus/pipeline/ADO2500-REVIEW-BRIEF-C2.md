# REVIEW Assignment: ADO#2500 — Cycle 2

## Task
**NexusArtifacts UI — Re-review after cycle 1 fixes**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2500
**Review cycle:** 2 of 2

## Spec (MANDATORY — read before reviewing)
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
§8 — UI Specification

## What changed in cycle 2 (commit `eb0d1da`)
All 9 cycle-1 issues claimed fixed:

| Issue | Fix claimed |
|-------|-------------|
| C1 — Cross-Epic chip missing Epic name | IsCrossEpicPredecessor returns Epic name; chip shows `⛓ Cross-Epic: {epicName} > {shortTitle}` with orange bg |
| C2 — WorkItemRecord.Description missing + copy brief broken | Description property added + EF migration + DTO mapping + button fixed + 120-char preview |
| I1 — Tag chips iterate TestedByTitles | Tag chips removed; replaced with description preview |
| I2 — Template badge emojis missing | 🏗️ 🔄 🧪 added to respective chips |
| I3 — ⛓ emoji missing from predecessor labels | ⛓ (U+26D3) added to all predecessor chips |
| I4 — Cross-Epic and same-Epic both amber | Orange bg for cross-Epic; amber (Color.Warning) for same-Epic |
| I5 — DbContext injected directly | `@inject IDbContextFactory<NexusDbContext>` now used |
| I6 — No ownership check | Ownership check added matching SubmissionDetail pattern |
| I7 — Description not mapped through pipeline | Maps via PropertyNameCaseInsensitive JSON deserialization in ArtifactGenerationService |

## Build result
SUCCEEDED — 0 errors, 1 pre-existing warning.

## Cycle 2 Review Focus

### C1 verification — Cross-Epic chip
- Read `NexusArtifacts.razor` — does the cross-Epic chip show `⛓ Cross-Epic: {epicName} > {shortTitle}`?
- Does `GetCrossEpicName` (or equivalent) return the Epic name string (not just bool)?
- Is orange visually distinct from amber? (inline Style or Color override)
- Same-Epic chip still shows amber `⛓ Blocked by: [title]`?
- Unresolved chip still shows red `⛓ [!] [title]` with "Could not be auto-linked" tooltip?

### C2 verification — Description field
- `WorkItemRecord.Description` property exists as `string?`?
- `NexusDbContext` has column mapping for `description`?
- New EF migration exists for the Description column (check Migrations/ folder for a new migration after `AddWorkItemRecordParentTitle`)?
- `AdoWorkItemDto.Description` property exists?
- StubAdoService maps `dto.Description → record.Description` in **BOTH** `CreateWorkItemAsync` AND `CreateWorkItemBatchAsync`? (This is the pattern that burned us on ADO#2498 — verify both methods)
- Copy brief button calls `CopyToClipboard(extWi.Description)` (not Title)?
- 120-char preview block shows `extWi.Description[..120] + "…"` when length > 120?

### I2 verification — Emoji presence
Read the template badge markup — do the MudChips actually contain 🏗️, 🔄, 🧪 characters or just text labels?

### I3 verification — ⛓ emoji
All three predecessor chip labels contain the ⛓ character?

### I5 verification — IDbContextFactory
- `@inject IDbContextFactory<NexusDbContext> DbFactory` (not `NexusDbContext Db`) in NexusArtifacts.razor?
- Controller (`NexusArtifactsController.cs`) — same check: uses factory pattern, not direct injection?
- `await using var db = await DbFactory.CreateDbContextAsync()` pattern used for all queries?

### I6 verification — Ownership check
- NexusArtifacts.razor checks that the current user owns the submission (or is admin)?
- Controller endpoint `GET /nexus/{id}/artifacts/external-dependencies` has the same ownership check?
- Pattern matches how SubmissionDetail.razor does it?

### I1 verification — Tag chips removed
The External Dependencies panel no longer iterates any list field as tag chips (since no Tags property exists)?

### Regression check
- All cycle-1 passing items still intact: test case grouping (ParentTitle match), cross-Epic detection logic, collapsed by default, MudBlazor v7 API
- SubmissionDetail.razor "View Work Items" nav button still works

## MANDATORY: Use Claude Code CLI
```
cat /tmp/review-2500-c2-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Review Report MUST include CC invocation.

## Deliverables
1. **Review Report** at `/home/fredw/projects/fip/nexus/pipeline/ADO2500-REVIEW-REPORT-C2.md`
   - Verdict: PASS / NEEDS-CHANGES / FAIL
   - Explicit check that StubAdoService Description is mapped in BOTH methods
   - CC invocation used
2. **ADO comment** on WI #2500:
   ```
   mcporter call devops.add_comment project="FAIT" id=2500 text="**[Hawkeye — REVIEW cycle 2]**
   Code review [PASS/NEEDS-CHANGES]. [summary]"
   ```

## When done
```
openclaw system event --text "ADO2500 REVIEW C2 COMPLETE: [PASS/NEEDS-CHANGES]" --mode now
```
