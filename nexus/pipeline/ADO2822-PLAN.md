# BUILD Assignment: ADO#2822
## NEXUS ADO Post: Post approved WI set to selected ADO project via "Post to ADO" action

**WI:** ADO#2822 | Project: Fortress | Feature: #2816 | Epic: #2793
**Risk:** medium | **Pipeline path:** full (Tony → Clint → Rhodey → Natasha)
**Spec file:** Jarvis task description (no separate spec file — details below)
**ADO attribution prefix for all comments:** `**[Tony Stark — BUILD cycle 1]**`

---

## Pre-read

Before coding, read:
1. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor` — existing page (Post to ADO button stub was added by ADO#2821)
2. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/StubAdoService.cs` — what CreateWorkItemBatchAsync does today
3. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/IAdoService.cs` — service interface
4. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/AdoCreationService.cs` — Phase 2 placeholder

---

## What This WI Adds

ADO#2821 added the Edit Mode tree editor and left a **disabled "Post to ADO" button stub** on `NexusArtifacts.razor`. This WI wires up that button.

The flow:
1. User clicks **Post to ADO** (enabled when Edit Mode is off, NexusAdmin role only)
2. A **confirmation dialog** appears with:
   - ADO **organization** field (pre-filled from `appsettings.json` `NexusAdo:Organization` or blank)
   - ADO **project selector** (dropdown loaded from `IAdoService.GetProjectsAsync()`)
   - Summary: "Post [N] work items to [Project]?"
   - [Cancel] [Post to ADO →] buttons
3. On confirm: call `IAdoService.CreateWorkItemBatchAsync(artifactSet, workItemDtos)` with all `WorkItemRecord` rows in the current artifact set
4. **Progress indicator** during posting (MudProgressLinear or MudCircularProgress with status text)
5. On completion: show **results panel** inline on the page:
   - For each WI: title + status chip (Created / Error) + live ADO link (if AdoWorkItemId > 0)
   - Summary count: "X created, Y errors"
6. **Write back to DB**: for each successfully created WI, update `WorkItemRecord.AdoWorkItemId` and `WorkItemRecord.AdoWorkItemUrl` (and `Status = "Created"`); for errors, set `Status = "Error"` + `ErrorDetail`

---

## Architecture Notes

### Service layer — use IAdoService (already DI-registered as StubAdoService)

`Program.cs` registers: `builder.Services.AddScoped<IAdoService, StubAdoService>();`

`CreateWorkItemBatchAsync` takes:
- `ArtifactSet artifactSet` (the current `_artifactSet`)
- `List<AdoWorkItemDto> items` — convert `WorkItemRecord` rows to `AdoWorkItemDto` list

**DO NOT switch to `AdoCreationService`** — that's Phase 2. Use `IAdoService` (which resolves to `StubAdoService` at runtime).

### WorkItemRecord → AdoWorkItemDto conversion

`AdoWorkItemDto` has:
- `WorkItemType`, `Title`, `Description`, `AcceptanceCriteria`, `ParentTitle`, `PredecessorTitles`
- `IsExternalDependency`, `ExternalOwner`, `WiTemplate`, `TestedByTitles`

Map `WorkItemRecord` fields to `AdoWorkItemDto` for the batch call. Check `AdoWorkItemDto.cs` for exact field names.

### Write-back to DB after posting

After `CreateWorkItemBatchAsync` returns the list of `WorkItemRecord` results:
1. For each result: find the matching DB record by `ArtifactSet.Id` + `Title` (or use the existing Id)
2. Update `AdoWorkItemId`, `AdoWorkItemUrl`, `Status`, `ErrorDetail`
3. `db.SaveChangesAsync()`

Use `IDbContextFactory<NexusDbContext>` (already injected in the page) to get a scoped DB context for the write-back.

### Auth

- **Post to ADO button visibility:** `NexusAdmin` only (unlike Edit Mode which is Admin + Reviewer)
- Use `UserContextService.IsAdminAsync()` (already in the page)
- API-level guard: not needed — the button calls the Blazor service directly (no new HTTP endpoint needed for this WI)

### ADO project list

`GetProjectsAsync(org)` from `StubAdoService` returns a hardcoded list: `["FAIT", "FIRM", "FORMS", "NEXUS"]`. The org string comes from config. If `NexusAdo:Organization` is not set in appsettings, default to `"FortressAffinityGroup"`.

Add to `appsettings.json` if not already present:
```json
"NexusAdo": {
  "Organization": "FortressAffinityGroup"
}
```

---

## Detailed Implementation

### 1. Confirmation Dialog component (inline or separate Razor file)

Options:
- **Inline MudDialog** triggered by `IDialogService` — preferred
- Or an inline `@if (_showConfirmDialog)` block with a MudPaper overlay

Fields:
```
Organization: [FortressAffinityGroup]  (MudTextField, read-only or editable)
ADO Project:  [dropdown from GetProjectsAsync]
──────────────────────────────────────────────
Post 144 work items to Fortress?
[Cancel] [Post to ADO →]
```

### 2. Progress state

```csharp
private bool _isPosting = false;
private string _postingStatus = "";
private List<WorkItemRecord>? _postResults = null;
```

While `_isPosting`:
- Show `MudProgressLinear Indeterminate="true"` + status text ("Posting [N] work items to ADO...")
- Post to ADO button shows a spinner / is disabled

### 3. Post method

```csharp
private async Task PostToAdoAsync()
{
    _isPosting = true;
    _postingStatus = $"Posting {_workItems.Count} work items to {_selectedProject}...";
    StateHasChanged();

    try
    {
        // Convert WorkItemRecord list to AdoWorkItemDto list
        var dtos = _workItems.Select(MapToDto).ToList();
        
        // Call service
        var results = await AdoService.CreateWorkItemBatchAsync(_artifactSet!, dtos);
        
        // Write results back to DB
        await WriteBackResultsAsync(results);
        
        _postResults = results;
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Post to ADO failed: {ex.Message}", Severity.Error);
    }
    finally
    {
        _isPosting = false;
        StateHasChanged();
    }
}
```

### 4. Results panel

After posting, show inline below the tree:

```
┌─────────────────────────────────────────────────────┐
│  ✅ ADO Post Complete — 144 created, 0 errors        │
│                                                      │
│  [WI Type] [Title]                   [✅ Created] [🔗] │
│  [WI Type] [Title]                   [✅ Created] [🔗] │
│  ...                                                 │
└─────────────────────────────────────────────────────┘
```

- Green `Created` chip for success, red `Error` chip for failure
- Link icon opens `AdoWorkItemUrl` in new tab (if non-empty)
- Failure rows show `ErrorDetail` in a tooltip on the Error chip

### 5. WorkItemRecord → AdoWorkItemDto mapping

```csharp
private static AdoWorkItemDto MapToDto(WorkItemRecord record) => new()
{
    WorkItemType = record.WorkItemType,
    Title = record.Title,
    Description = record.Description,
    AcceptanceCriteria = record.AcceptanceCriteria,
    ParentTitle = record.ParentTitle,
    PredecessorTitles = record.PredecessorTitles,
    IsExternalDependency = record.IsExternalDependency,
    ExternalOwner = record.ExternalOwner,
    WiTemplate = record.WiTemplate,
    TestedByTitles = record.TestedByTitles
};
```

Check `AdoWorkItemDto.cs` for exact property names before writing.

---

## Acceptance Criteria

1. A user with `NexusAdmin` role sees the enabled "Post to ADO" button when Edit Mode is off
2. Clicking "Post to ADO" opens a confirmation dialog with ADO org (pre-filled) and project dropdown
3. Project dropdown is populated from `IAdoService.GetProjectsAsync()`
4. Confirming triggers `CreateWorkItemBatchAsync` with all WorkItemRecords in the current ArtifactSet
5. A progress indicator displays during posting
6. On completion, a results panel shows each WI with status chip (Created/Error) and ADO link
7. Successfully created WIs have `AdoWorkItemId` and `AdoWorkItemUrl` written back to DB
8. Error WIs have `Status = "Error"` and `ErrorDetail` written back to DB
9. "Post to ADO" button is disabled while Edit Mode is active (tooltip: "Exit edit mode to post")
10. A user without `NexusAdmin` role cannot see or activate the "Post to ADO" button

---

## Inject into NexusArtifacts.razor

Add injections (if not already present from ADO#2821):
```razor
@inject IAdoService AdoService
@inject IDialogService DialogService
```

`IAdoService` is already registered as `StubAdoService` in DI.

---

## Build Report Format

```markdown
# Build Report — ADO#2822

## CC Invocation
`cat brief.md | claude --model sonnet --print --dangerously-skip-permissions`

## Changes
- Files modified (list each)
- New files (if any)

## AC Checklist
1. NexusAdmin sees Post to ADO button when Edit Mode off — [PASS/FAIL]
2. Confirmation dialog with org + project dropdown — [PASS/FAIL]
3. Project dropdown from GetProjectsAsync — [PASS/FAIL]
4. CreateWorkItemBatchAsync called with all WIs — [PASS/FAIL]
5. Progress indicator during posting — [PASS/FAIL]
6. Results panel with status + ADO link — [PASS/FAIL]
7. AdoWorkItemId/Url written back to DB — [PASS/FAIL]
8. Error WIs get Status=Error + ErrorDetail — [PASS/FAIL]
9. Post to ADO disabled during Edit Mode — [PASS/FAIL]
10. Non-Admin cannot see button — [PASS/FAIL]

## Self-review checklist
- [ ] WorkItemRecord → AdoWorkItemDto mapping includes all fields
- [ ] DB write-back uses SaveChangesAsync inside transaction
- [ ] IAdoService used (NOT AdoCreationService directly)
- [ ] Post to ADO disabled while _editMode = true
- [ ] Progress indicator visible during async post
- [ ] Results panel renders after completion
- [ ] Snackbar on exception
- [ ] ADO link opens in new tab (_blank)
```

---

## ADO Comment

```
mcporter call devops.add_comment project=Fortress id=2822 text="**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: Post to ADO button wired, confirmation dialog, project dropdown, progress indicator, results panel with ADO write-back. Build: SUCCEEDED."
```

---

## MANDATORY: Use Claude Code CLI

```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30

cat brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/nexus/`
