# REVIEW Assignment: ADO#2499

## Task
**Implement cross-Epic predecessor linking in AdoCreationService**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2499
**Review cycle:** 1 of 2

## Spec (MANDATORY — read before reviewing)
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Focus on **§6 — Service Layer Changes**, subsection **AdoCreationService — Predecessor Resolution**. The spec has the exact code pattern to compare against.

## Files Modified by Tony (commit `73dab07`)

| File | Action |
|------|--------|
| `src/FortressNexus.Web/Services/StubAdoService.cs` | Modified — batch ordering + two-pass predecessor resolution |
| `src/FortressNexus.Web/Services/AdoCreationService.cs` | Created — Phase 2 placeholder with one-at-a-time predecessor linking |

## Build result
SUCCEEDED — 0 errors, 1 pre-existing warning.

## Tony's approach
- **StubAdoService:** Two-pass. All records saved first, then second pass resolves predecessors using title→ID map. IDs come from EF-assigned `WorkItemRecord.Id`.
- **AdoCreationService:** One-at-a-time. Each WI created via ADO API, ID registered immediately, predecessors resolved per WI. Phase 2 placeholder.
- Batch ordering: `Epic=0, Feature=1, User Story=2, Task=3, Test Case=4` sort before processing.

## Review Focus

### 1. Batch ordering — both services
- Is the sort applied BEFORE the creation loop in both `StubAdoService.CreateWorkItemBatchAsync` and `AdoCreationService`?
- Does the sort cover all 5 types: Epic, Feature, User Story, Task, Test Case?
- Is there a sensible default for unknown types (should not crash)?

### 2. Title→ADO ID map — both services
- Is `Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)` used (case-insensitive)?
- Is the map populated AFTER each WI is created/saved (so the ID is real, not 0)?
- For StubAdoService two-pass: are IDs populated from `WorkItemRecord.Id` post-`SaveChangesAsync`? Verify EF actually assigns IDs after save before the second pass reads them.

### 3. Predecessor resolution loop — both services
- Does it iterate `record.PredecessorTitles ?? []` (null-safe)?
- On RESOLVED: logs at Information level with predecessor title + ID + WI title?
- On UNRESOLVED: logs warning AND adds comment "Predecessor '[title]' could not be auto-linked — please add manually."?
- For StubAdoService: is the unresolved comment path a log statement or does it attempt a real ADO call? (Log is correct for stub; real ADO call would be wrong here)
- For AdoCreationService: does it stub or defer the actual ADO predecessor link API call appropriately for Phase 2?

### 4. StubAdoService two-pass correctness
This is the highest-risk part. Verify:
- Pass 1: all WorkItemRecords created and saved — confirm `await _db.SaveChangesAsync()` (or equivalent) is called before pass 2
- Pass 2: title→ID map built from saved records (EF-assigned IDs, not 0)
- Pass 2: predecessor resolution loop runs over saved records
- Are the WorkItemRecords from pass 1 still in scope for pass 2? (No variable shadowing issues)

### 5. Existing StubAdoService behavior unchanged
- Does `CreateWorkItemAsync` (single WI method) still work as before? Predecessor resolution is batch-only — single creates don't need the map.
- Are all existing field mappings from ADO#2498 (WiTemplate, IsExternalDependency, ExternalOwner, TestedByTitles, ParentTitle, PredecessorTitles) still present?

### 6. AdoCreationService — scope check
This is a new file created as a Phase 2 placeholder. Verify:
- It implements the same interface as StubAdoService (or its own interface)
- The predecessor resolution pattern matches spec §6 code sample
- It doesn't accidentally do anything real (no live ADO API calls that could fire in dev)

## MANDATORY: Use Claude Code CLI
```
cat /tmp/review-2499-brief.txt | claude --model sonnet --print --dangerously-skip-permissions
```
Review Report MUST include CC invocation. Do NOT reason about code without CC reading it first.

## Deliverables
1. **Review Report** at `/home/fredw/projects/fip/nexus/pipeline/ADO2499-REVIEW-REPORT.md`
   - Verdict: PASS / NEEDS-CHANGES / FAIL
   - Explicit confirmation of two-pass correctness (EF IDs assigned before pass 2)
   - CC invocation used
2. **ADO comment** on WI #2499:
   ```
   mcporter call devops.add_comment project="FAIT" id=2499 text="**[Hawkeye — REVIEW cycle 1]**
   Code review [PASS/NEEDS-CHANGES]. [summary]"
   ```

## When done
```
openclaw system event --text "ADO2499 REVIEW COMPLETE: [PASS/NEEDS-CHANGES]" --mode now
```
