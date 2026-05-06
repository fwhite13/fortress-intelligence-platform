# BUILD Plan — ADO#2820
## Decomp Trigger: Approved submission shows Decompose button, triggers ArtifactGenerationService, persists to DB

**WI:** ADO#2820 | Feature #2815 | Epic #2793  
**Repo:** `/home/fredw/projects/fip/nexus/`  
**Spec Ref:** `nexus-decomp-upgrade-spec-2026-04-27.md`

---

## Context

`SubmissionDetail.razor` already has a "Generate Work Items" button rendered for `Approved` and `ArtifactsCreated` statuses. However the current `HandleGenerateWorkItems` method:
1. Creates an in-memory stub `ArtifactSet` that is NEVER saved to the database
2. Passes it to `AdoService.CreateWorkItemBatchAsync` (StubAdoService) which creates in-memory records NEVER saved to DB
3. Does not update submission status to `ArtifactsCreated` 
4. Is visible to any user with access — not gated to NexusAdmin/NexusReviewer
5. Has no progress state display — just a loading spinner

The result: the tree editor (NexusArtifacts page, ADO#2821) has nothing to show because no WorkItemRecords exist in the database.

---

## Required Changes

### 1. New `IArtifactGenerationService` method: `DecomposeAndPersistAsync`

Add to `IArtifactGenerationService.cs`:
```csharp
Task<ArtifactSet> DecomposeAndPersistAsync(int submissionId, int specDocumentId, string callerUpn);
```

Implement in `ArtifactGenerationService.cs`:
```csharp
public async Task<ArtifactSet> DecomposeAndPersistAsync(int submissionId, int specDocumentId, string callerUpn)
{
    // 1. Call existing GenerateWorkItemsAsync to get DTOs from Bedrock
    var dtos = await GenerateWorkItemsAsync(specDocumentId);

    // 2. Create and persist ArtifactSet
    var artifactSet = new ArtifactSet
    {
        SpecDocumentId = specDocumentId,
        AdoOrganization = "FortressAffinityGroup",
        AdoProjectName = "Fortress",
        ProcessTemplateTypeId = "6b724908-ef14-45cf-84f8-768b5384da45",
        ExternalDependencyCount = dtos.Count(d => d.IsExternalDependency),
        CreatedAt = DateTime.UtcNow,
        CreatedBy = callerUpn
    };
    _db.ArtifactSets.Add(artifactSet);
    await _db.SaveChangesAsync();  // get artifactSet.Id

    // 3. Map DTOs to WorkItemRecords and save
    var records = dtos.Select(dto => new WorkItemRecord
    {
        ArtifactSetId = artifactSet.Id,
        WorkItemType = dto.WorkItemType,
        Title = dto.Title,
        Description = dto.Description,
        AcceptanceCriteria = dto.AcceptanceCriteria,
        Status = "Pending",
        WiTemplate = dto.WiTemplate,
        IsExternalDependency = dto.IsExternalDependency,
        ExternalOwner = dto.ExternalOwner,
        TestedByTitles = dto.TestedByTitles,
        ParentTitle = dto.ParentTitle,
        PredecessorTitles = dto.PredecessorTitles
    }).ToList();
    _db.WorkItemRecords.AddRange(records);
    await _db.SaveChangesAsync();

    // 4. Update submission status to ArtifactsCreated
    var submission = await _db.Submissions.FindAsync(submissionId)
        ?? throw new InvalidOperationException($"Submission {submissionId} not found");
    submission.Status = SubmissionStatus.ArtifactsCreated;
    await _db.SaveChangesAsync();

    return artifactSet;
}
```

**Note:** `ArtifactGenerationService` already has `_db` injected (NexusDbContext). Check the constructor — if `_db` is not already there, inject `NexusDbContext` directly (not `IDbContextFactory` — this is a scoped service, not Blazor Server page).

### 2. Update `SubmissionDetail.razor` — replace `HandleGenerateWorkItems`

```csharp
private async Task HandleGenerateWorkItems()
{
    if (_activeSpec is null || _submission is null) return;
    if (!_isEditor) return;  // guard — NexusAdmin or NexusReviewer only
    
    _isGeneratingWorkItems = true;
    _generatingStatusText = "Running decomposition (this may take 30-60 seconds)...";
    StateHasChanged();

    try
    {
        var callerUpn = await UserContextService.GetUpnAsync();
        var artifactSet = await ArtifactGenerationService.DecomposeAndPersistAsync(
            _submission.Id, _activeSpec.Id, callerUpn);
        
        // Reload submission to reflect ArtifactsCreated status
        _submission = await SubmissionService.GetByIdAsync(_submission.Id);
        _generatingStatusText = null;
        Snackbar.Add($"Decomposition complete — {artifactSet.ExternalDependencyCount} external dependencies flagged. Navigate to Work Items to review.", Severity.Success);
    }
    catch (Exception ex)
    {
        _generatingStatusText = null;
        Snackbar.Add($"Decomposition failed: {ex.Message}", Severity.Error);
    }
    finally
    {
        _isGeneratingWorkItems = false;
        StateHasChanged();
    }
}
```

Add `_isEditor` field (initialized in `OnInitializedAsync`):
```csharp
private bool _isEditor;
// In OnInitializedAsync after loading submission:
_isEditor = await UserContextService.IsNexusEditorAsync();
```

Add `_generatingStatusText` field:
```csharp
private string? _generatingStatusText;
```

### 3. Update the UI button section in `SubmissionDetail.razor`

Replace the existing button markup for `Approved` / `ArtifactsCreated` status:

```razor
@if ((_submission.Status == SubmissionStatus.Approved) && _isEditor)
{
    <MudButton Variant="Variant.Filled"
               Color="Color.Secondary"
               StartIcon="@Icons.Material.Filled.AutoAwesome"
               OnClick="HandleGenerateWorkItems"
               Disabled="@_isGeneratingWorkItems"
               Class="nexus-detail-generate-wi-btn">
        @(_isGeneratingWorkItems ? "Decomposing..." : "Decompose")
    </MudButton>
    
    @if (_isGeneratingWorkItems && _generatingStatusText is not null)
    {
        <MudText Typo="Typo.caption" Color="Color.Secondary" Class="nexus-detail-decomp-status">
            @_generatingStatusText
        </MudText>
    }
}

@if (_submission.Status == SubmissionStatus.ArtifactsCreated)
{
    <MudButton Variant="Variant.Filled"
               Color="Color.Success"
               StartIcon="@Icons.Material.Filled.ListAlt"
               OnClick="@(() => Nav.NavigateTo($"/nexus/{Id}/artifacts"))"
               Class="nexus-detail-view-wi-btn">
        Review Work Items
    </MudButton>
}
```

Remove the old stub `AdoService.CreateWorkItemBatchAsync` call — it's no longer needed from this page.

### 4. Remove orphaned ADO call from SubmissionDetail.razor

The existing `HandleGenerateWorkItems` calls `AdoService.CreateWorkItemBatchAsync(stubArtifactSet, workItems)`. Remove this — the new `DecomposeAndPersistAsync` handles persistence directly without going through the ADO service stub.

If `AdoService` is only injected for this one call and no other purpose, remove the injection from the page.

---

## Acceptance Criteria (all must pass)

- [ ] NexusAdmin and NexusReviewer see Decompose button on Approved submissions
- [ ] NexusUser cannot see or trigger Decompose
- [ ] Clicking Decompose shows "Decomposing..." button text and status caption during Bedrock call
- [ ] On success: ArtifactSet and WorkItemRecords persisted to DB; submission status = `ArtifactsCreated`; "Review Work Items" button appears
- [ ] On failure: error snackbar shown; button re-enables for retry
- [ ] "Review Work Items" button navigates to `/nexus/{id}/artifacts`
- [ ] No regressions on existing Download / Delete / Review Spec buttons

---

## Files to change

- `src/FortressNexus.Web/Services/IArtifactGenerationService.cs` — add `DecomposeAndPersistAsync`
- `src/FortressNexus.Web/Services/ArtifactGenerationService.cs` — implement it
- `src/FortressNexus.Web/Components/Pages/SubmissionDetail.razor` — replace HandleGenerateWorkItems, update UI

---

## Key types

```csharp
// UserContextService
Task<bool> IsNexusEditorAsync()  // Admin || Reviewer — added by ADO#2821

// ArtifactGenerationService._db — already NexusDbContext (check constructor)
// If not injected, add: private readonly NexusDbContext _db;

// SubmissionStatus enum values in use:
// Approved → decomp triggers → ArtifactsCreated

// AdoWorkItemDto fields (check for AcceptanceCriteria field):
// - added in ADO#2497 / migration 20260506000001
// Check that AcceptanceCriteria is present in AdoWorkItemDto or add it
```

---

## CC env vars
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
```

## ADO Comment format
```
**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {summary}. Build: SUCCEEDED.
```
`mcporter call devops.add_comment project=Fortress id=2820 text="..."`
