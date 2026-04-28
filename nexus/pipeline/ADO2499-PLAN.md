# BUILD Assignment: ADO#2499

## Task
**Implement cross-Epic predecessor linking in AdoCreationService**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2499

## MANDATORY: Read the spec first
Read the full spec before starting ANY code changes:
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Focus on **§6 — Service Layer Changes**, specifically the **AdoCreationService — Predecessor Resolution** subsection. It has the exact code pattern to implement.

## Repo
`/home/fredw/projects/fip/nexus/`
Working directory: `src/FortressNexus.Web/`

## Prerequisites already deployed
- `WorkItemRecord.PredecessorTitles` (`List<string>?`) — ADO#2497
- `AdoWorkItemDto.PredecessorTitles` — ADO#2498
- `StubAdoService` maps `dto.PredecessorTitles → record.PredecessorTitles` — ADO#2498

## Step 0: Reconnaissance — read the existing services first
Before writing any code, read:
1. `Services/AdoCreationService.cs` (or whatever the live ADO creation service is named) — understand its current structure, what interface it implements, how it creates WIs
2. `Services/StubAdoService.cs` — understand the existing batch creation method `CreateWorkItemBatchAsync` which already handles the DTO→WorkItemRecord mapping

Phase 1 uses `StubAdoService` for all WI creation. `AdoCreationService` may exist as a placeholder/stub for Phase 2. Implement the predecessor resolution in BOTH regardless.

## What to build

### 1. Batch ordering — Epics → Features → User Stories → Tasks → Test Cases

In both `AdoCreationService` and `StubAdoService`, when `CreateWorkItemBatchAsync` receives a list of DTOs, sort them before processing:

```csharp
var orderedWis = workItems
    .OrderBy(w => w.Type switch {
        "Epic" => 0,
        "Feature" => 1,
        "User Story" => 2,
        "Task" => 3,
        "Test Case" => 4,
        _ => 5
    })
    .ToList();
```

This ensures when we build the title→ID map sequentially, Epics are registered before Features before Stories, maximizing same-batch resolution for predecessor links.

### 2. Title→ADO ID map

In both services, before the creation loop, initialize:
```csharp
var titleToAdoId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
```

After each WI is created, register it:
```csharp
titleToAdoId[wi.Title] = createdAdoId;
```

For `StubAdoService`, "createdAdoId" is the `WorkItemRecord.Id` assigned by EF after `SaveChangesAsync`. For `AdoCreationService`, it's the ADO work item ID returned by the ADO API.

### 3. Predecessor resolution loop

From spec §6 — implement this exact pattern in both services:

```csharp
foreach (var predecessorTitle in wi.PredecessorTitles ?? [])
{
    if (titleToAdoId.TryGetValue(predecessorTitle, out int predecessorAdoId))
    {
        // AdoCreationService: add ADO predecessor link relationship via ADO API
        // StubAdoService: log resolution
        _logger.LogInformation(
            "Predecessor '{PredTitle}' resolved to ID {PredId} for WI '{WiTitle}'",
            predecessorTitle, predecessorAdoId, wi.Title);
    }
    else
    {
        _logger.LogWarning(
            "Predecessor '{PredTitle}' could not be resolved for WI '{WiTitle}'",
            predecessorTitle, wi.Title);
        // Both services: add comment to the created WI
        await AddCommentAsync(createdId,
            $"Predecessor '{predecessorTitle}' could not be auto-linked — please add manually.");
    }
}
```

For `StubAdoService`, `AddCommentAsync` means adding an ADO comment via mcporter/the ADO API — or if that's not wired in StubAdoService, log it clearly: `_logger.LogWarning("UNRESOLVED PREDECESSOR: ...")`.

For `AdoCreationService`, the predecessor link relationship type in ADO is `"System.LinkTypes.Dependency-Forward"` (predecessor) or `"System.LinkTypes.Dependency-Reverse"` (successor) — check existing ADO link code if any exists in the service, otherwise implement via the ADO patch API.

### 4. Important: PredecessorTitles on WorkItemRecord vs DTO

The resolution loop processes `WorkItemRecord.PredecessorTitles` (already mapped from DTO in ADO#2498). When iterating the batch in `CreateWorkItemBatchAsync`, use the `WorkItemRecord` entities (post-save, after EF assigns IDs) to build the title map. The ordering step should happen on the DTO list before creating records.

If the current batch method creates all records in one `SaveChangesAsync` call, you'll need to either:
- Process one at a time (save after each, register ID, then process predecessors), OR
- Save all first (get IDs), then do a second pass for predecessor links

The second-pass approach is cleaner for StubAdoService since it avoids N individual saves. For AdoCreationService (live ADO), one-at-a-time is standard.

Read the existing `CreateWorkItemBatchAsync` implementation carefully and choose the approach that fits its current structure.

## ADO Updates (MANDATORY)
After implementing, add a comment to ADO WI #2499:
```
mcporter call devops.add_comment project="FAIT" id=2499 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {summary}. Build: SUCCEEDED."
```

## Build Report required
Create `/home/fredw/projects/fip/nexus/pipeline/ADO2499-BUILD-REPORT.md` with:
- Files modified (with full paths)
- Commit hash
- Build result (`dotnet build` output)
- CC invocation command used
- Which save strategy was used (one-at-a-time vs two-pass) and why
- Self-review checklist: all 5 AC items verified

## Notify Maria when done
When completely finished, run:
openclaw system event --text "ADO2499 BUILD COMPLETE: predecessor linking in AdoCreationService + StubAdoService" --mode now
