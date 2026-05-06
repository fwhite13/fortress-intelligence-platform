# CC Brief — ADO#2822: Wire Up "Post to ADO" Button on NexusArtifacts.razor

## Context

You are working on `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor`.

ADO#2821 already committed and left a **disabled "Post to ADO" button stub** on this page. Your job is to:
1. Wire up that button with a confirmation dialog, progress indicator, results panel, and DB write-back.
2. Add `@inject IAdoService AdoService` and ensure `IConfiguration` is injected for reading the ADO org config.
3. Add `NexusAdo:Organization` key to `appsettings.json` if not present.

**CRITICAL CONSTRAINTS:**
- Use `IAdoService` (DI-registered as `StubAdoService`) — do NOT call `AdoCreationService` directly.
- Use CSS classes — no inline styles except where already existing in the file (e.g., border-left for MudPaper nodes is already present — leave those alone).
- Post to ADO button is NexusAdmin-only (use `_isAdmin` flag — see below). 
- Post to ADO button must be disabled when `_editMode = true`.
- All async methods use `StateHasChanged()` to trigger re-render.

---

## Files to Read First

Before making any changes, read these files:

1. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor` — the current state
2. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/IAdoService.cs` — service interface
3. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/StubAdoService.cs` — what CreateWorkItemBatchAsync returns
4. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Models/DTOs/AdoWorkItemDto.cs` — DTO fields
5. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Models/Entities/WorkItemRecord.cs` — entity fields
6. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/UserContextService.cs` — to confirm IsAdminAsync() exists
7. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/appsettings.json` — check if NexusAdo section exists

---

## Changes Required

### 1. appsettings.json — add NexusAdo section if missing

In `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/appsettings.json`, add after the `"Bedrock"` block (or wherever appropriate at the root level):

```json
"NexusAdo": {
  "Organization": "FortressAffinityGroup"
}
```

Only add if not already present.

---

### 2. NexusArtifacts.razor — Injections

Add these inject directives at the top of the file (after existing @inject lines):

```razor
@inject IAdoService AdoService
@inject IConfiguration Configuration
```

Note: `IDialogService DialogService` is already injected. Check before adding again.

---

### 3. NexusArtifacts.razor — Replace the existing "Post to ADO" button

The current stub button is:

```razor
<MudTooltip Text="@(_editMode ? "Exit edit mode to post" : "Post work items to ADO")">
    <MudButton Variant="Variant.Filled"
               Color="Color.Primary"
               StartIcon="@Icons.Material.Filled.CloudUpload"
               Size="Size.Small"
               Disabled="@_editMode">
        Post to ADO
    </MudButton>
</MudTooltip>
```

Replace it with:

```razor
@if (_isAdmin)
{
    <MudTooltip Text="@(_editMode ? "Exit edit mode to post" : (_isPosting ? "Posting in progress..." : "Post work items to ADO"))">
        <MudButton Variant="Variant.Filled"
                   Color="Color.Primary"
                   StartIcon="@Icons.Material.Filled.CloudUpload"
                   Size="Size.Small"
                   Disabled="@(_editMode || _isPosting)"
                   OnClick="OpenAdoConfirmDialog">
            Post to ADO
        </MudButton>
    </MudTooltip>
}
```

---

### 4. NexusArtifacts.razor — Progress indicator and Results panel

Add these two blocks **after the closing `</MudExpansionPanels>` tag** of the WI tree (the one that contains the `@foreach (var epic in _epics)` loop), and **before the closing `</MudContainer>` tag**:

```razor
@* ── ADO Post Progress ── *@
@if (_isPosting)
{
    <MudPaper Class="mt-4 pa-4">
        <MudText Typo="Typo.body1" Class="mb-2">@_postingStatus</MudText>
        <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
    </MudPaper>
}

@* ── ADO Post Results Panel ── *@
@if (_postResults is not null)
{
    var createdCount = _postResults.Count(r => r.Status == "Created");
    var errorCount = _postResults.Count(r => r.Status == "Error");

    <MudPaper Class="mt-4 pa-4">
        <MudText Typo="Typo.h6" Class="mb-3">
            @(errorCount == 0 ? "✅" : "⚠️") ADO Post Complete — @createdCount created, @errorCount errors
        </MudText>
        <MudTable Items="_postResults" Dense="true" Hover="true" Striped="true">
            <HeaderContent>
                <MudTh>Type</MudTh>
                <MudTh>Title</MudTh>
                <MudTh>Status</MudTh>
                <MudTh>ADO Link</MudTh>
            </HeaderContent>
            <RowTemplate>
                <MudTd>@context.WorkItemType</MudTd>
                <MudTd>@context.Title</MudTd>
                <MudTd>
                    @if (context.Status == "Created")
                    {
                        <MudChip T="string" Color="Color.Success" Size="Size.Small">Created</MudChip>
                    }
                    else
                    {
                        <MudTooltip Text="@(context.ErrorDetail ?? "Unknown error")">
                            <MudChip T="string" Color="Color.Error" Size="Size.Small">Error</MudChip>
                        </MudTooltip>
                    }
                </MudTd>
                <MudTd>
                    @if (!string.IsNullOrEmpty(context.AdoWorkItemUrl))
                    {
                        <MudIconButton Icon="@Icons.Material.Filled.OpenInNew"
                                       Size="Size.Small"
                                       Href="@context.AdoWorkItemUrl"
                                       Target="_blank"
                                       Color="Color.Primary" />
                    }
                </MudTd>
            </RowTemplate>
        </MudTable>
    </MudPaper>
}
```

---

### 5. NexusArtifacts.razor — @code block additions

#### 5a. New private fields — add after the existing field declarations (after the `_acLists` dictionary declaration):

```csharp
// ADO Post state
private bool _isAdmin = false;
private bool _isPosting = false;
private string _postingStatus = "";
private List<WorkItemRecord>? _postResults = null;
private string _adoOrg = "";
private string _selectedAdoProject = "";
private List<string> _adoProjects = new();
```

#### 5b. In `OnInitializedAsync`, after the line `_isEditor = await UserContextService.IsNexusEditorAsync();`, add:

```csharp
_isAdmin = await UserContextService.IsAdminAsync();
_adoOrg = Configuration["NexusAdo:Organization"] ?? "FortressAffinityGroup";
```

#### 5c. Add new methods at the end of the @code block (before the final closing `}`):

```csharp
// ── ADO Post Methods ──

private async Task OpenAdoConfirmDialog()
{
    // Load projects for the dropdown
    _adoProjects = await AdoService.GetProjectsAsync(_adoOrg);
    if (_adoProjects.Count > 0)
        _selectedAdoProject = _adoProjects[0];

    var parameters = new DialogParameters<AdoConfirmDialog>
    {
        { x => x.Organization, _adoOrg },
        { x => x.Projects, _adoProjects },
        { x => x.SelectedProject, _selectedAdoProject },
        { x => x.WorkItemCount, _workItems.Count }
    };

    var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
    var dialog = await DialogService.ShowAsync<AdoConfirmDialog>("Post to ADO", parameters, options);
    var result = await dialog.Result;

    if (result is { Canceled: false } && result.Data is string selectedProject)
    {
        _selectedAdoProject = selectedProject;
        await PostToAdoAsync();
    }
}

private async Task PostToAdoAsync()
{
    _isPosting = true;
    _postResults = null;
    _postingStatus = $"Posting {_workItems.Count} work items to {_selectedAdoProject}...";
    StateHasChanged();

    try
    {
        var dtos = _workItems.Select(MapToAdoDto).ToList();
        var results = await AdoService.CreateWorkItemBatchAsync(_artifactSet!, dtos);
        await WriteBackAdoResultsAsync(results);
        _postResults = results;
        Snackbar.Add($"Posted {results.Count(r => r.Status == "Created")} work items to ADO.", Severity.Success);
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

private async Task WriteBackAdoResultsAsync(List<WorkItemRecord> results)
{
    // Build lookup: Title → result record (stub returns new records, not the DB records)
    var resultsByTitle = results.ToDictionary(r => r.Title, r => r, StringComparer.OrdinalIgnoreCase);

    await using var db = await DbFactory.CreateDbContextAsync();
    var dbRecords = await db.WorkItemRecords
        .Where(w => w.ArtifactSetId == _artifactSet!.Id)
        .ToListAsync();

    foreach (var dbRecord in dbRecords)
    {
        if (resultsByTitle.TryGetValue(dbRecord.Title, out var result))
        {
            dbRecord.AdoWorkItemId = result.AdoWorkItemId;
            dbRecord.AdoWorkItemUrl = result.AdoWorkItemUrl;
            dbRecord.Status = result.Status;
            dbRecord.ErrorDetail = result.ErrorDetail;

            // Update in-memory local record too
            var local = _workItems.FirstOrDefault(w => w.Id == dbRecord.Id);
            if (local is not null)
            {
                local.AdoWorkItemId = result.AdoWorkItemId;
                local.AdoWorkItemUrl = result.AdoWorkItemUrl;
                local.Status = result.Status;
                local.ErrorDetail = result.ErrorDetail;
            }
        }
    }

    await db.SaveChangesAsync();
}

private static AdoWorkItemDto MapToAdoDto(WorkItemRecord record) => new()
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

---

### 6. Create new Razor dialog component: `AdoConfirmDialog.razor`

Create `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Components/Dialogs/AdoConfirmDialog.razor` with this content:

```razor
@using MudBlazor

<MudDialog>
    <DialogContent>
        <MudStack Spacing="3">
            <MudTextField Value="@Organization"
                          Label="ADO Organization"
                          Variant="Variant.Outlined"
                          ReadOnly="true" />
            <MudSelect T="string"
                       Label="ADO Project"
                       @bind-Value="SelectedProject"
                       Variant="Variant.Outlined">
                @foreach (var project in Projects)
                {
                    <MudSelectItem T="string" Value="@project">@project</MudSelectItem>
                }
            </MudSelect>
            <MudText Typo="Typo.body1">
                Post <strong>@WorkItemCount</strong> work items to <strong>@SelectedProject</strong>?
            </MudText>
        </MudStack>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel" Color="Color.Default" Variant="Variant.Text">Cancel</MudButton>
        <MudButton OnClick="Confirm"
                   Color="Color.Primary"
                   Variant="Variant.Filled"
                   StartIcon="@Icons.Material.Filled.CloudUpload"
                   Disabled="@string.IsNullOrEmpty(SelectedProject)">
            Post to ADO →
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = default!;

    [Parameter] public string Organization { get; set; } = "";
    [Parameter] public List<string> Projects { get; set; } = new();
    [Parameter] public string SelectedProject { get; set; } = "";
    [Parameter] public int WorkItemCount { get; set; }

    private void Cancel() => MudDialog.Cancel();
    private void Confirm() => MudDialog.Close(DialogResult.Ok(SelectedProject));
}
```

Ensure the `Components/Dialogs/` directory is created if it doesn't exist.

---

## Acceptance Criteria to Verify After Changes

1. `_isAdmin` field is declared and populated from `UserContextService.IsAdminAsync()` in `OnInitializedAsync`
2. Post to ADO button is wrapped in `@if (_isAdmin)` — non-admins don't see it
3. Post to ADO button has `OnClick="OpenAdoConfirmDialog"` and `Disabled="@(_editMode || _isPosting)"`
4. `OpenAdoConfirmDialog` calls `AdoService.GetProjectsAsync(_adoOrg)` and opens `AdoConfirmDialog`
5. `PostToAdoAsync` calls `AdoService.CreateWorkItemBatchAsync(_artifactSet!, dtos)` with all `_workItems` mapped via `MapToAdoDto`
6. `WriteBackAdoResultsAsync` updates `AdoWorkItemId`, `AdoWorkItemUrl`, `Status`, `ErrorDetail` in DB and in-memory
7. Progress indicator (`MudProgressLinear`) shown while `_isPosting = true`
8. Results panel shows table with status chips and ADO links after posting
9. `AdoConfirmDialog.razor` created in `Components/Dialogs/`
10. `NexusAdo:Organization` added to `appsettings.json`
11. `@inject IAdoService AdoService` and `@inject IConfiguration Configuration` added to page

---

## Build Verification

After making all changes, run:

```bash
cd /home/fredw/projects/fip/nexus/src/FortressNexus.Web && dotnet build 2>&1 | tail -30
```

Fix any compile errors. Common issues to watch for:
- `AdoConfirmDialog` DialogParameters lambda must use the correct property names
- `IMudDialogInstance` is the correct MudBlazor 7 interface name (not `IDialogReference`)
- `@foreach` in razor needs proper scoping for `context.AdoWorkItemUrl` in MudTable
- Ensure `using FortressNexus.Web.Services;` is in scope for `IAdoService` on the Razor page (check existing @using statements)

Once build is clean, run the pre-flight script:

```bash
bash /home/fredw/.openclaw/workspace/scripts/preflight/git-commit.sh
```

Then commit:

```bash
cd /home/fredw/projects/fip/nexus && git add -A && git commit -m "feat(ADO#2822): ADO post action — confirmation dialog, progress, results write-back"
```

Output the full commit hash at the end.
