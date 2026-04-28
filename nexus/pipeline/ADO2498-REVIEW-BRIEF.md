# REVIEW Assignment: ADO#2498

## Task
**Integrate IWiClassifier into ArtifactGenerationService**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2498
**Review cycle:** 1 of 2

## Spec (MANDATORY — read before reviewing)
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Focus on **§5 — Component Map** (ArtifactGenerationService modifications) and **§6 — Service Layer Changes**.

## Files Modified by Tony (commit `d4c0656`)

| File | Action |
|------|--------|
| `src/FortressNexus.Web/Models/DTOs/AdoWorkItemDto.cs` | Modified — classification fields added |
| `src/FortressNexus.Web/Services/ArtifactGenerationService.cs` | Modified — IWiClassifier injection, classification loop, TC generation |
| `src/FortressNexus.Web/Services/StubAdoService.cs` | Modified — DTO→WorkItemRecord mapping + ExternalDependencyCount |

## Build Result
- Build: SUCCEEDED (0 errors, 1 pre-existing warning)
- Commit: `d4c0656`

## Tony's Approach (understand before reviewing)
Tony extended `AdoWorkItemDto` with classification fields (`WiTemplate`, `IsExternalDependency`, `ExternalOwner`, `TestedByTitles`) and set them in `ArtifactGenerationService` on the DTO. Then `StubAdoService.CreateWorkItemBatchAsync` maps DTO→WorkItemRecord, sets `ExternalDependencyCount` on the ArtifactSet, and saves. This is a DTO-pipeline approach rather than direct WorkItemRecord manipulation.

## ⚠️ Critical: Verify the data actually reaches the DB

The spec calls for classification data to be stored in `WorkItemRecord`. Tony's approach adds an extra hop: DTO → WorkItemRecord mapping in StubAdoService. You MUST verify:

1. **StubAdoService DTO→WorkItemRecord mapping** — does it copy ALL new fields?
   - `dto.WiTemplate` → `record.WiTemplate`
   - `dto.IsExternalDependency` → `record.IsExternalDependency`
   - `dto.ExternalOwner` → `record.ExternalOwner`
   - `dto.TestedByTitles` → `record.TestedByTitles`
   - `dto.WiType == "Test Case"` → `record.WiType = "Test Case"`
   - `dto.ParentTitle` → `record.ParentTitle` (for generated Test Case WIs)

2. **ExternalDependencyCount** — is it set on the `ArtifactSet` entity before `SaveChangesAsync`? Not on the DTO, not after save — BEFORE save.

3. **Test Case WorkItemRecords** — are generated TCs actually saved to the DB? Trace through: ArtifactGenerationService generates TC DTOs → StubAdoService receives them → creates WorkItemRecord entries for them → they get added to the EF context and saved. Verify the full path.

## Review Checklist

### 1. ArtifactGenerationService.cs
- Is `IWiClassifier` injected via constructor (not newed up)?
- Is `ClassifyStory` called for every parsed WI candidate — ALL types (Epic, Feature, Story, Task), not just Stories?
- Is `ShouldGenerateTestCases` called only for User Story type candidates?
- AC parsing: does it handle `- [ ]` checkbox lines AND numbered list lines AND newline fallback?
- Are Test Case DTOs given correct `WiType = "Test Case"`, `WiTemplate = WiTemplateType.TestCase`, and `ParentTitle` = parent story's exact title?
- Is `TestedByTitles` populated on the parent story DTO with the TC titles?
- Is existing generation behavior for standard WIs unchanged?

### 2. AdoWorkItemDto.cs
- Are all 4 new fields present: `WiTemplate`, `IsExternalDependency`, `ExternalOwner`, `TestedByTitles`?

### 3. StubAdoService.cs
- Does DTO→WorkItemRecord mapping copy ALL new fields (see critical check above)?
- Is `ExternalDependencyCount` set on `ArtifactSet` BEFORE `SaveChangesAsync`?
- Are Test Case WorkItemRecords added to the EF context and saved?
- Is the `PredecessorTitles` field also mapped (Tony may have missed it — check)?

### 4. Spec compliance check
Spec §5 says `ArtifactGenerationService` is where classification and TC generation happen, and `StubAdoService` is where predecessor resolution + tagging + "Tested By" relationship happens. Verify Tony's split matches this — classification in ArtifactGenerationService ✅, DB persistence path through StubAdoService. If Tony did the mapping correctly, this is fine. If any classification logic leaked into StubAdoService instead of ArtifactGenerationService, flag it.

### 5. No regressions
- Are any existing constructor parameters or service dependencies in ArtifactGenerationService still intact?
- Does StubAdoService still handle the existing WorkItemRecord fields it was handling before?

## MANDATORY: Use Claude Code CLI
```
cat /tmp/review-2498-brief.txt | claude --model sonnet --print --dangerously-skip-permissions
```
Your Review Report MUST include the CC invocation used. Do NOT reason about code without CC reading it first.

## Deliverables
1. **Review Report** at `/home/fredw/projects/fip/nexus/pipeline/ADO2498-REVIEW-REPORT.md`
   - Verdict: PASS / NEEDS-CHANGES / FAIL
   - Explicit confirmation that classification data reaches WorkItemRecord (or finding if it doesn't)
   - CC invocation used
2. **ADO comment** on WI #2498:
   ```
   mcporter call devops.add_comment project="FAIT" id=2498 text="**[Hawkeye — REVIEW cycle 1]**
   Code review [PASS/NEEDS-CHANGES]. [summary]"
   ```

## When done
```
openclaw system event --text "ADO2498 REVIEW COMPLETE: [PASS/NEEDS-CHANGES]" --mode now
```
