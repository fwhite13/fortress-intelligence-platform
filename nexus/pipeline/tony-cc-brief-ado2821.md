# CC Brief: ADO#2821 — NEXUS Decomp Tree Editor

You are implementing the **NEXUS Decomp Tree Editor** for ADO#2821. This is an inline hierarchy editor added on top of the existing `NexusArtifacts.razor` read-only tree view.

## Repository
Working directory: `/home/fredw/projects/fip/nexus/`
All source in: `src/FortressNexus.Web/`

## Summary of Changes Required

### 1. WorkItemRecord entity — add AcceptanceCriteria field
File: `src/FortressNexus.Web/Models/Entities/WorkItemRecord.cs`

Add this property after `Description`:
```csharp
// Acceptance criteria — newline-delimited string (e.g. "Item 1\nItem 2\nItem 3")
public string? AcceptanceCriteria { get; set; }
```

### 2. NexusDbContext — map AcceptanceCriteria column
File: `src/FortressNexus.Web/Data/NexusDbContext.cs`

In the `WorkItemRecord` entity configuration (inside the `modelBuilder.Entity<WorkItemRecord>(entity => { ... })` block), add after the `Description` property mapping:
```csharp
entity.Property(e => e.AcceptanceCriteria).HasColumnName("acceptance_criteria").HasColumnType("text");
```

### 3. EF Migration — AddAcceptanceCriteriaToWorkItemRecord
Create a new migration file. The project already has migrations up to `20260428171338_AddWorkItemRecordDescription`. 

Create file: `src/FortressNexus.Web/Migrations/20260506000001_AddAcceptanceCriteriaToWorkItemRecord.cs`
```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressNexus.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptanceCriteriaToWorkItemRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "acceptance_criteria",
                table: "work_item_records",
                type: "text",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "acceptance_criteria",
                table: "work_item_records");
        }
    }
}
```

ALSO create the Designer file: `src/FortressNexus.Web/Migrations/20260506000001_AddAcceptanceCriteriaToWorkItemRecord.Designer.cs`

Copy the pattern from `20260428171338_AddWorkItemRecordDescription.Designer.cs`. The new Designer file should be:
- Partial class name: `AddAcceptanceCriteriaToWorkItemRecord`
- MigrationAttribute: `"20260506000001_AddAcceptanceCriteriaToWorkItemRecord"`
- In `BuildTargetModel`, copy the WorkItemRecord entity snapshot from the existing Designer and add the new `acceptance_criteria` column property. 

Read `src/FortressNexus.Web/Migrations/20260428171338_AddWorkItemRecordDescription.Designer.cs` to get the base — then add the `acceptance_criteria` column to the WorkItemRecord columns in `BuildTargetModel`.

### 4. UserContextService — add IsNexusEditorAsync()
File: `src/FortressNexus.Web/Services/UserContextService.cs`

Add this method:
```csharp
public async Task<bool> IsNexusEditorAsync()
{
    var authState = await _authStateProvider.GetAuthenticationStateAsync();
    return authState.User.IsInRole(NexusRoles.Admin) || authState.User.IsInRole(NexusRoles.Reviewer);
}
```

### 5. NexusArtifactsController — add 6 new editor endpoints
File: `src/FortressNexus.Web/Controllers/NexusArtifactsController.cs`

**Architecture note:** 
- `WorkItemRecord` uses `ParentTitle` (string) for hierarchy — NO `ParentId` FK.
- `UserContextService` is currently registered as scoped and injected into Razor components via DI, but controllers use constructor injection. However, `UserContextService` depends on `AuthenticationStateProvider` which is Blazor-specific and NOT available in controller context. 
- **Solution:** In controller, use `User.IsInRole()` directly from `ControllerBase.User` instead of `UserContextService`. Use `User.IsInRole(NexusRoles.Admin) || User.IsInRole(NexusRoles.Reviewer)` for the editor check.

The existing controller is:
```csharp
using FortressNexus.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FortressNexus.Web.Controllers;

[ApiController]
[Authorize]
[Route("nexus/{id:int}/artifacts")]
public class NexusArtifactsController : ControllerBase
{
    private readonly NexusDbContext _db;

    public NexusArtifactsController(NexusDbContext db)
    {
        _db = db;
    }
    // ... existing GetExternalDependencies endpoint
}
```

Replace the entire controller file with the extended version. Keep the existing `GetExternalDependencies` endpoint unchanged and add the following.

**Add these DTOs inside the namespace (before the controller class):**
```csharp
public record PatchTitleRequest(string Title);
public record PatchDescriptionRequest(string Description);
public record PatchAcRequest(string AcceptanceCriteria);
public record PatchParentRequest(string ParentTitle);
public record CreateWiRequest(
    int ArtifactSetId,
    string WorkItemType,
    string Title,
    string? ParentTitle
);
```

**Add these 6 endpoints to the controller:**

```csharp
// PATCH wi/{wiId}/title — update title + cascade ParentTitle references in same ArtifactSet
[HttpPatch("wi/{wiId:int}/title")]
public async Task<IActionResult> PatchTitle(int id, int wiId, [FromBody] PatchTitleRequest req)
{
    if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
        return Forbid();

    if (string.IsNullOrWhiteSpace(req.Title))
        return BadRequest("Title cannot be empty.");

    await using var tx = await _db.Database.BeginTransactionAsync();
    try
    {
        var wi = await _db.WorkItemRecords.FirstOrDefaultAsync(w => w.Id == wiId);
        if (wi is null) return NotFound();

        // Ownership check
        if (!await VerifySubmissionAccessAsync(id, wi.ArtifactSetId))
            return Forbid();

        var oldTitle = wi.Title;
        wi.Title = req.Title;

        // Cascade: update all ParentTitle references in the same ArtifactSet
        if (oldTitle != req.Title)
        {
            var dependents = await _db.WorkItemRecords
                .Where(w => w.ArtifactSetId == wi.ArtifactSetId && w.ParentTitle == oldTitle)
                .ToListAsync();
            foreach (var dep in dependents)
                dep.ParentTitle = req.Title;
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return Ok(wi);
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
}

// PATCH wi/{wiId}/description
[HttpPatch("wi/{wiId:int}/description")]
public async Task<IActionResult> PatchDescription(int id, int wiId, [FromBody] PatchDescriptionRequest req)
{
    if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
        return Forbid();

    var wi = await _db.WorkItemRecords.FirstOrDefaultAsync(w => w.Id == wiId);
    if (wi is null) return NotFound();

    if (!await VerifySubmissionAccessAsync(id, wi.ArtifactSetId))
        return Forbid();

    wi.Description = req.Description;
    await _db.SaveChangesAsync();
    return Ok(wi);
}

// PATCH wi/{wiId}/ac
[HttpPatch("wi/{wiId:int}/ac")]
public async Task<IActionResult> PatchAc(int id, int wiId, [FromBody] PatchAcRequest req)
{
    if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
        return Forbid();

    var wi = await _db.WorkItemRecords.FirstOrDefaultAsync(w => w.Id == wiId);
    if (wi is null) return NotFound();

    if (!await VerifySubmissionAccessAsync(id, wi.ArtifactSetId))
        return Forbid();

    wi.AcceptanceCriteria = req.AcceptanceCriteria;
    await _db.SaveChangesAsync();
    return Ok(wi);
}

// PATCH wi/{wiId}/parent — reparent WI
[HttpPatch("wi/{wiId:int}/parent")]
public async Task<IActionResult> PatchParent(int id, int wiId, [FromBody] PatchParentRequest req)
{
    if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
        return Forbid();

    var wi = await _db.WorkItemRecords.FirstOrDefaultAsync(w => w.Id == wiId);
    if (wi is null) return NotFound();

    if (!await VerifySubmissionAccessAsync(id, wi.ArtifactSetId))
        return Forbid();

    // Validate target parent exists in same ArtifactSet
    var targetParent = await _db.WorkItemRecords
        .FirstOrDefaultAsync(w => w.ArtifactSetId == wi.ArtifactSetId && w.Title == req.ParentTitle);
    if (targetParent is null)
        return BadRequest($"Parent WI with title '{req.ParentTitle}' not found in artifact set.");

    wi.ParentTitle = req.ParentTitle;
    await _db.SaveChangesAsync();
    return Ok(wi);
}

// POST wi — create new WI
[HttpPost("wi")]
public async Task<IActionResult> CreateWi(int id, [FromBody] CreateWiRequest req)
{
    if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
        return Forbid();

    if (!await VerifySubmissionAccessByArtifactSetAsync(id, req.ArtifactSetId))
        return Forbid();

    var wi = new FortressNexus.Web.Models.Entities.WorkItemRecord
    {
        ArtifactSetId = req.ArtifactSetId,
        WorkItemType = req.WorkItemType,
        Title = req.Title,
        ParentTitle = req.ParentTitle,
        Status = "Created",
        AdoWorkItemId = 0,
        AdoWorkItemUrl = "",
        WiTemplate = FortressNexus.Web.Services.WiTemplateType.Standard
    };

    _db.WorkItemRecords.Add(wi);
    await _db.SaveChangesAsync();
    return Ok(wi);
}

// DELETE wi/{wiId} — cascade delete all descendants
[HttpDelete("wi/{wiId:int}")]
public async Task<IActionResult> DeleteWi(int id, int wiId)
{
    if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
        return Forbid();

    var wi = await _db.WorkItemRecords.FirstOrDefaultAsync(w => w.Id == wiId);
    if (wi is null) return NotFound();

    if (!await VerifySubmissionAccessAsync(id, wi.ArtifactSetId))
        return Forbid();

    // Load all WIs in the same ArtifactSet to walk the tree
    var allWis = await _db.WorkItemRecords
        .Where(w => w.ArtifactSetId == wi.ArtifactSetId)
        .ToListAsync();

    // Collect WI + all descendants recursively by ParentTitle
    var toDelete = new List<FortressNexus.Web.Models.Entities.WorkItemRecord>();
    CollectDescendants(wi, allWis, toDelete);
    toDelete.Add(wi);

    _db.WorkItemRecords.RemoveRange(toDelete);
    await _db.SaveChangesAsync();

    return Ok(new { deleted = toDelete.Count });
}

// Helper: collect all descendants recursively by ParentTitle
private static void CollectDescendants(
    FortressNexus.Web.Models.Entities.WorkItemRecord parent,
    List<FortressNexus.Web.Models.Entities.WorkItemRecord> all,
    List<FortressNexus.Web.Models.Entities.WorkItemRecord> result)
{
    var children = all.Where(w => w.ParentTitle == parent.Title && w.Id != parent.Id).ToList();
    foreach (var child in children)
    {
        result.Add(child);
        CollectDescendants(child, all, result);
    }
}

// Helper: verify submission ownership and that ArtifactSet belongs to submission
private async Task<bool> VerifySubmissionAccessAsync(int submissionId, int artifactSetId)
{
    var submission = await _db.Submissions.FirstOrDefaultAsync(s => s.Id == submissionId);
    if (submission is null) return false;

    var currentUpn = User.FindFirst("preferred_username")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

    if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
        && !User.IsInRole(NexusRoles.Admin))
        return false;

    var artifactSet = await _db.ArtifactSets.FirstOrDefaultAsync(a => a.Id == artifactSetId);
    if (artifactSet is null) return false;

    var specDoc = await _db.SpecDocuments.FirstOrDefaultAsync(sd => sd.Id == artifactSet.SpecDocumentId);
    if (specDoc is null) return false;

    return specDoc.SubmissionId == submissionId;
}

private async Task<bool> VerifySubmissionAccessByArtifactSetAsync(int submissionId, int artifactSetId)
{
    return await VerifySubmissionAccessAsync(submissionId, artifactSetId);
}
```

**Required using statements to add at the top of the controller file:**
```csharp
using FortressNexus.Web.Services;
using FortressNexus.Web.Models.Entities;
```

### 6. NexusArtifacts.razor — Add Edit Mode

File: `src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor`

**IMPORTANT:** Do NOT rewrite this file. Extend it. The existing file is 367 lines. Read it carefully and make targeted changes.

Here is the complete replacement file. It preserves all existing functionality and adds Edit Mode on top.

**New inject directives to add at the top (after the existing @inject lines):**
```razor
@inject IDialogService DialogService
@inject NavigationManager Nav
```
Wait — `Nav` is already injected. Add only `IDialogService DialogService`.

**New @using statements** — add after the existing @using lines:
```razor
@using System.Net.Http
@using System.Net.Http.Json
@using System.Text.Json
```

**State fields to add in the @code block** (after `private string? _error;`):
```csharp
// Edit Mode state
private bool _editMode = false;
private bool _isEditor = false;
// Track original titles for revert on save failure
private Dictionary<int, string> _originalTitles = new();
private Dictionary<int, string> _originalDescriptions = new();
private Dictionary<int, string> _originalAc = new();
// AC list state (per WI id → list of ac strings)
private Dictionary<int, List<string>> _acLists = new();
```

**In OnInitializedAsync**, after the line `_epics = _workItems.Where(w => w.WorkItemType == "Epic").ToList();` and before the closing `}` of the try block, add:
```csharp
_isEditor = await UserContextService.IsNexusEditorAsync();
// Snapshot originals for revert on save failure
foreach (var w in _workItems)
{
    _originalTitles[w.Id] = w.Title;
    _originalDescriptions[w.Id] = w.Description ?? "";
    _originalAc[w.Id] = w.AcceptanceCriteria ?? "";
    _acLists[w.Id] = (w.AcceptanceCriteria ?? "")
        .Split('\n', StringSplitOptions.None)
        .Where(s => !string.IsNullOrEmpty(s))
        .ToList();
}
```

**Replace the header block** in the Razor markup. Find this in the existing file:
```razor
<MudText Typo="Typo.h5" Class="mb-4">Work Items — @_submissionTitle</MudText>
```
Replace with:
```razor
<div class="d-flex align-center justify-space-between mb-4 flex-wrap gap-2">
    <div class="d-flex align-center gap-2">
        <MudText Typo="Typo.h5">Work Items — @_submissionTitle</MudText>
        @if (_editMode)
        {
            <MudChip T="string" Color="Color.Warning" Size="Size.Small" Class="ml-2">✏️ EDIT MODE</MudChip>
        }
    </div>
    <div class="d-flex align-center gap-2">
        @if (_isEditor && !_editMode)
        {
            <MudButton Variant="Variant.Outlined"
                       Color="Color.Primary"
                       StartIcon="@Icons.Material.Filled.Edit"
                       Size="Size.Small"
                       OnClick="@(() => { _editMode = true; StateHasChanged(); })">
                Edit
            </MudButton>
        }
        @if (_editMode)
        {
            <MudButton Variant="Variant.Filled"
                       Color="Color.Success"
                       StartIcon="@Icons.Material.Filled.CheckCircle"
                       Size="Size.Small"
                       OnClick="@(() => { _editMode = false; StateHasChanged(); })">
                Done Editing
            </MudButton>
        }
        <MudTooltip Text="@(_editMode ? "Exit edit mode to post" : "Post work items to ADO")">
            <MudButton Variant="Variant.Filled"
                       Color="Color.Primary"
                       StartIcon="@Icons.Material.Filled.CloudUpload"
                       Size="Size.Small"
                       Disabled="@_editMode">
                Post to ADO
            </MudButton>
        </MudTooltip>
    </div>
</div>
```

**For the Epic WI row** — find this block in the existing markup:
```razor
<TitleContent>
    <div class="d-flex align-center gap-2">
        @RenderTemplateBadge(epic)
        <MudText Typo="Typo.subtitle1"><strong>Epic:</strong> @epic.Title</MudText>
        @RenderPredecessorBadges(epic)
    </div>
</TitleContent>
```
Replace with:
```razor
<TitleContent>
    <div class="d-flex align-center gap-2 flex-wrap">
        @RenderTemplateBadge(epic)
        @if (_editMode)
        {
            <MudText Typo="Typo.subtitle2" Class="mr-1"><strong>Epic:</strong></MudText>
            <MudTextField @bind-Value="epic.Title"
                          Variant="Variant.Outlined"
                          Margin="Margin.Dense"
                          Style="min-width:300px"
                          @onblur="@(() => SaveTitle(epic))"
                          @onkeydown="@((KeyboardEventArgs e) => { if (e.Key == \"Enter\") InvokeAsync(() => SaveTitle(epic)); })"
                          @onclick:stopPropagation="true" />
        }
        else
        {
            <MudText Typo="Typo.subtitle1"><strong>Epic:</strong> @epic.Title</MudText>
        }
        @RenderPredecessorBadges(epic)
        @if (_editMode)
        {
            <MudIconButton Icon="@Icons.Material.Filled.Delete"
                           Color="Color.Error"
                           Size="Size.Small"
                           OnClick="@(() => ConfirmDelete(epic))"
                           @onclick:stopPropagation="true" />
        }
    </div>
</TitleContent>
```

**For the Epic ChildContent block** — after the existing closing `</MudPaper>` of the last feature (i.e. after the `@foreach (var feature in features)` block ends), and before the `</ChildContent>` closing tag of the epic, add:
```razor
@if (_editMode)
{
    <div class="ml-4 mt-2">
        <MudButton Variant="Variant.Text"
                   Color="Color.Primary"
                   StartIcon="@Icons.Material.Filled.Add"
                   Size="Size.Small"
                   OnClick="@(() => AddWi("Feature", epic.Title))">
            + Add Feature
        </MudButton>
    </div>

    @* Description section for Epic *@
    <MudExpansionPanels Class="ml-4 mt-1">
        <MudExpansionPanel Text="Description ▾">
            <MudTextField @bind-Value="epic.Description"
                          Variant="Variant.Outlined"
                          Lines="3"
                          Label="Epic Description"
                          @onblur="@(() => SaveDescription(epic))" />
        </MudExpansionPanel>
    </MudExpansionPanels>
}
```

**For the Feature WI row** — find:
```razor
<div class="d-flex align-center gap-2 mb-1">
    @RenderTemplateBadge(feature)
    <MudText Typo="Typo.subtitle2"><strong>Feature:</strong> @feature.Title</MudText>
    @RenderPredecessorBadges(feature)
</div>
```
Replace with:
```razor
<div class="d-flex align-center gap-2 mb-1 flex-wrap">
    @RenderTemplateBadge(feature)
    @if (_editMode)
    {
        <MudText Typo="Typo.subtitle2" Class="mr-1"><strong>Feature:</strong></MudText>
        <MudTextField @bind-Value="feature.Title"
                      Variant="Variant.Outlined"
                      Margin="Margin.Dense"
                      Style="min-width:260px"
                      @onblur="@(() => SaveTitle(feature))"
                      @onkeydown="@((KeyboardEventArgs e) => { if (e.Key == \"Enter\") InvokeAsync(() => SaveTitle(feature)); })" />
        <MudMenu Label="Move ▾" Size="Size.Small" Dense="true">
            @foreach (var target in GetValidParents(feature))
            {
                <MudMenuItem OnClick="@(() => MoveWi(feature, target.Title))">@target.Title</MudMenuItem>
            }
        </MudMenu>
        <MudIconButton Icon="@Icons.Material.Filled.Delete"
                       Color="Color.Error"
                       Size="Size.Small"
                       OnClick="@(() => ConfirmDelete(feature))" />
    }
    else
    {
        <MudText Typo="Typo.subtitle2"><strong>Feature:</strong> @feature.Title</MudText>
    }
    @RenderPredecessorBadges(feature)
</div>
@if (_editMode)
{
    <MudExpansionPanels Class="ml-2 mb-1">
        <MudExpansionPanel Text="Description ▾">
            <MudTextField @bind-Value="feature.Description"
                          Variant="Variant.Outlined"
                          Lines="3"
                          Label="Feature Description"
                          @onblur="@(() => SaveDescription(feature))" />
        </MudExpansionPanel>
    </MudExpansionPanels>
}
```

**After the stories foreach loop ends** (after `</MudPaper>` that wraps each story) and before the closing `</MudPaper>` of the feature, add:
```razor
@if (_editMode)
{
    <div class="ml-4 mt-1">
        <MudButton Variant="Variant.Text"
                   Color="Color.Secondary"
                   StartIcon="@Icons.Material.Filled.Add"
                   Size="Size.Small"
                   OnClick="@(() => AddWi("User Story", feature.Title))">
            + Add Story
        </MudButton>
    </div>
}
```

**For the Story WI row** — find:
```razor
<div class="d-flex align-center flex-wrap gap-2 mb-1">
    @RenderTemplateBadge(story)
    <MudText Typo="Typo.body1"><strong>Story:</strong> @story.Title</MudText>
    @RenderPredecessorBadges(story)
</div>
```
Replace with:
```razor
<div class="d-flex align-center flex-wrap gap-2 mb-1">
    @RenderTemplateBadge(story)
    @if (_editMode)
    {
        <MudText Typo="Typo.body2" Class="mr-1"><strong>Story:</strong></MudText>
        <MudTextField @bind-Value="story.Title"
                      Variant="Variant.Outlined"
                      Margin="Margin.Dense"
                      Style="min-width:260px"
                      @onblur="@(() => SaveTitle(story))"
                      @onkeydown="@((KeyboardEventArgs e) => { if (e.Key == \"Enter\") InvokeAsync(() => SaveTitle(story)); })" />
        <MudMenu Label="Move ▾" Size="Size.Small" Dense="true">
            @foreach (var target in GetValidParents(story))
            {
                <MudMenuItem OnClick="@(() => MoveWi(story, target.Title))">@target.Title</MudMenuItem>
            }
        </MudMenu>
        <MudIconButton Icon="@Icons.Material.Filled.Delete"
                       Color="Color.Error"
                       Size="Size.Small"
                       OnClick="@(() => ConfirmDelete(story))" />
    }
    else
    {
        <MudText Typo="Typo.body1"><strong>Story:</strong> @story.Title</MudText>
    }
    @RenderPredecessorBadges(story)
</div>
@if (_editMode)
{
    <MudExpansionPanels Class="ml-2 mb-1">
        <MudExpansionPanel Text="Description ▾">
            <MudTextField @bind-Value="story.Description"
                          Variant="Variant.Outlined"
                          Lines="3"
                          Label="Story Description"
                          @onblur="@(() => SaveDescription(story))" />
        </MudExpansionPanel>
        <MudExpansionPanel Text="Acceptance Criteria ▾">
            @{
                if (!_acLists.ContainsKey(story.Id))
                    _acLists[story.Id] = new List<string>();
                var acItems = _acLists[story.Id];
            }
            @for (int acIdx = 0; acIdx < acItems.Count; acIdx++)
            {
                var capturedIdx = acIdx;
                <div class="d-flex align-center gap-1 mb-1">
                    <MudTextField Value="@acItems[capturedIdx]"
                                  ValueChanged="@((string val) => { acItems[capturedIdx] = val; })"
                                  Variant="Variant.Outlined"
                                  Margin="Margin.Dense"
                                  Style="flex:1"
                                  @onblur="@(() => SaveAc(story))" />
                    <MudIconButton Icon="@Icons.Material.Filled.Close"
                                   Size="Size.Small"
                                   Color="Color.Default"
                                   OnClick="@(() => { acItems.RemoveAt(capturedIdx); story.AcceptanceCriteria = string.Join('\n', acItems); SaveAc(story); })" />
                </div>
            }
            <MudButton Variant="Variant.Text"
                       Color="Color.Primary"
                       StartIcon="@Icons.Material.Filled.Add"
                       Size="Size.Small"
                       OnClick="@(() => { acItems.Add(""); StateHasChanged(); })">
                + Add AC
            </MudButton>
        </MudExpansionPanel>
    </MudExpansionPanels>
}
```

**For Task WI rows** — find:
```razor
<div class="d-flex align-center flex-wrap gap-2 ml-4 mb-1">
    @RenderTemplateBadge(task)
    <MudIcon Icon="@Icons.Material.Filled.CheckBox" Size="Size.Small" />
    <MudText Typo="Typo.body2">@task.Title</MudText>
    @RenderPredecessorBadges(task)
</div>
```
Replace with:
```razor
<div class="d-flex align-center flex-wrap gap-2 ml-4 mb-1">
    @RenderTemplateBadge(task)
    <MudIcon Icon="@Icons.Material.Filled.CheckBox" Size="Size.Small" />
    @if (_editMode)
    {
        <MudTextField @bind-Value="task.Title"
                      Variant="Variant.Outlined"
                      Margin="Margin.Dense"
                      Style="min-width:240px"
                      @onblur="@(() => SaveTitle(task))"
                      @onkeydown="@((KeyboardEventArgs e) => { if (e.Key == \"Enter\") InvokeAsync(() => SaveTitle(task)); })" />
        <MudMenu Label="Move ▾" Size="Size.Small" Dense="true">
            @foreach (var target in GetValidParents(task))
            {
                <MudMenuItem OnClick="@(() => MoveWi(task, target.Title))">@target.Title</MudMenuItem>
            }
        </MudMenu>
        <MudIconButton Icon="@Icons.Material.Filled.Delete"
                       Color="Color.Error"
                       Size="Size.Small"
                       OnClick="@(() => ConfirmDelete(task))" />
    }
    else
    {
        <MudText Typo="Typo.body2">@task.Title</MudText>
    }
    @RenderPredecessorBadges(task)
</div>
```

**After all tasks in the story (after the `@foreach (var task in tasks)` block) and before the Test Cases section**, add:
```razor
@if (_editMode)
{
    <div class="ml-4 mt-1">
        <MudButton Variant="Variant.Text"
                   Color="Color.Default"
                   StartIcon="@Icons.Material.Filled.Add"
                   Size="Size.Small"
                   OnClick="@(() => AddWi("Task", story.Title))">
            + Add Task
        </MudButton>
    </div>
}
```

**For Test Case TC rows** — find inside the TC foreach:
```razor
<div class="ml-4 mb-2">
    <div class="d-flex align-center gap-2">
        <MudChip T="string" Color="Color.Primary" Size="Size.Small">TC</MudChip>
        <MudText Typo="Typo.body2">@tc.Title</MudText>
    </div>
</div>
```
Replace with:
```razor
<div class="ml-4 mb-2">
    <div class="d-flex align-center gap-2">
        <MudChip T="string" Color="Color.Primary" Size="Size.Small">TC</MudChip>
        @if (_editMode)
        {
            <MudTextField @bind-Value="tc.Title"
                          Variant="Variant.Outlined"
                          Margin="Margin.Dense"
                          Style="min-width:220px"
                          @onblur="@(() => SaveTitle(tc))"
                          @onkeydown="@((KeyboardEventArgs e) => { if (e.Key == \"Enter\") InvokeAsync(() => SaveTitle(tc)); })" />
            <MudMenu Label="Move ▾" Size="Size.Small" Dense="true">
                @foreach (var target in GetValidParents(tc))
                {
                    <MudMenuItem OnClick="@(() => MoveWi(tc, target.Title))">@target.Title</MudMenuItem>
                }
            </MudMenu>
            <MudIconButton Icon="@Icons.Material.Filled.Delete"
                           Color="Color.Error"
                           Size="Size.Small"
                           OnClick="@(() => ConfirmDelete(tc))" />
        }
        else
        {
            <MudText Typo="Typo.body2">@tc.Title</MudText>
        }
    </div>
</div>
```

**After the TC foreach block** (inside the TC expansion panel ChildContent), add:
```razor
@if (_editMode)
{
    <div class="ml-4 mt-1">
        <MudButton Variant="Variant.Text"
                   Color="Color.Primary"
                   StartIcon="@Icons.Material.Filled.Add"
                   Size="Size.Small"
                   OnClick="@(() => AddWi("Test Case", story.Title))">
            + Add Test Case
        </MudButton>
    </div>
}
```

### 7. @code block additions for NexusArtifacts.razor

Add these methods to the @code block (after the existing `CopyToClipboard` method, before the `RenderTemplateBadge` static methods):

```csharp
// ── Edit Mode Helpers ──

private List<WorkItemRecord> GetValidParents(WorkItemRecord wi)
{
    return wi.WorkItemType switch
    {
        "Feature" => _workItems.Where(w => w.WorkItemType == "Epic").ToList(),
        "User Story" => _workItems.Where(w => w.WorkItemType == "Feature").ToList(),
        "Task" => _workItems.Where(w => w.WorkItemType == "User Story").ToList(),
        "Test Case" => _workItems.Where(w => w.WorkItemType == "User Story").ToList(),
        _ => new List<WorkItemRecord>()
    };
}

private List<WorkItemRecord> GetAllDescendants(WorkItemRecord wi)
{
    var result = new List<WorkItemRecord>();
    CollectDescendantsLocal(wi, result);
    return result;
}

private void CollectDescendantsLocal(WorkItemRecord parent, List<WorkItemRecord> result)
{
    var children = _workItems.Where(w => w.ParentTitle == parent.Title && w.Id != parent.Id).ToList();
    foreach (var child in children)
    {
        result.Add(child);
        CollectDescendantsLocal(child, result);
    }
}

// ── Save Methods ──

private async Task SaveTitle(WorkItemRecord wi)
{
    if (string.IsNullOrWhiteSpace(wi.Title))
    {
        wi.Title = _originalTitles.GetValueOrDefault(wi.Id, wi.Title);
        Snackbar.Add("Title cannot be empty — reverted.", Severity.Warning);
        return;
    }

    var oldTitle = _originalTitles.GetValueOrDefault(wi.Id, wi.Title);
    if (oldTitle == wi.Title) return; // no change

    try
    {
        var response = await CallApiAsync(
            HttpMethod.Patch,
            $"nexus/{Id}/artifacts/wi/{wi.Id}/title",
            new { title = wi.Title });

        if (response.IsSuccessStatusCode)
        {
            // Cascade update ParentTitle refs in local list
            foreach (var other in _workItems.Where(w => w.ParentTitle == oldTitle))
                other.ParentTitle = wi.Title;
            _originalTitles[wi.Id] = wi.Title;
            StateHasChanged();
        }
        else
        {
            wi.Title = oldTitle;
            Snackbar.Add("Save failed — check connection.", Severity.Error);
        }
    }
    catch
    {
        wi.Title = _originalTitles.GetValueOrDefault(wi.Id, wi.Title);
        Snackbar.Add("Save failed — check connection.", Severity.Error);
    }
}

private async Task SaveDescription(WorkItemRecord wi)
{
    try
    {
        var response = await CallApiAsync(
            HttpMethod.Patch,
            $"nexus/{Id}/artifacts/wi/{wi.Id}/description",
            new { description = wi.Description ?? "" });

        if (response.IsSuccessStatusCode)
            _originalDescriptions[wi.Id] = wi.Description ?? "";
        else
        {
            wi.Description = _originalDescriptions.GetValueOrDefault(wi.Id, "");
            Snackbar.Add("Save failed — check connection.", Severity.Error);
        }
    }
    catch
    {
        wi.Description = _originalDescriptions.GetValueOrDefault(wi.Id, "");
        Snackbar.Add("Save failed — check connection.", Severity.Error);
    }
}

private async Task SaveAc(WorkItemRecord wi)
{
    if (!_acLists.TryGetValue(wi.Id, out var items)) return;
    var joined = string.Join('\n', items.Where(s => !string.IsNullOrWhiteSpace(s)));
    wi.AcceptanceCriteria = joined;

    try
    {
        var response = await CallApiAsync(
            HttpMethod.Patch,
            $"nexus/{Id}/artifacts/wi/{wi.Id}/ac",
            new { acceptanceCriteria = joined });

        if (response.IsSuccessStatusCode)
            _originalAc[wi.Id] = joined;
        else
        {
            var orig = _originalAc.GetValueOrDefault(wi.Id, "");
            wi.AcceptanceCriteria = orig;
            _acLists[wi.Id] = orig.Split('\n', StringSplitOptions.None)
                .Where(s => !string.IsNullOrEmpty(s)).ToList();
            Snackbar.Add("Save failed — check connection.", Severity.Error);
        }
    }
    catch
    {
        Snackbar.Add("Save failed — check connection.", Severity.Error);
    }
}

private async Task MoveWi(WorkItemRecord wi, string newParentTitle)
{
    try
    {
        var response = await CallApiAsync(
            HttpMethod.Patch,
            $"nexus/{Id}/artifacts/wi/{wi.Id}/parent",
            new { parentTitle = newParentTitle });

        if (response.IsSuccessStatusCode)
        {
            wi.ParentTitle = newParentTitle;
            // Rebuild epics/lists from _workItems
            _epics = _workItems.Where(w => w.WorkItemType == "Epic").ToList();
            StateHasChanged();
        }
        else
            Snackbar.Add("Move failed — check connection.", Severity.Error);
    }
    catch
    {
        Snackbar.Add("Move failed — check connection.", Severity.Error);
    }
}

private async Task ConfirmDelete(WorkItemRecord wi)
{
    var descendants = GetAllDescendants(wi);
    string message = descendants.Count > 0
        ? $"Delete '{wi.Title}' and its {descendants.Count} child item(s)? This cannot be undone."
        : $"Delete '{wi.Title}'? This cannot be undone.";

    string yesText = descendants.Count > 0 ? "Delete with children" : "Delete";

    bool? confirmed = await DialogService.ShowMessageBox(
        "Confirm Delete",
        message,
        yesText: yesText,
        cancelText: "Cancel");

    if (confirmed != true) return;

    try
    {
        var response = await CallApiAsync(
            HttpMethod.Delete,
            $"nexus/{Id}/artifacts/wi/{wi.Id}",
            null);

        if (response.IsSuccessStatusCode)
        {
            // Remove from local list
            _workItems.Remove(wi);
            foreach (var d in descendants)
                _workItems.Remove(d);
            _epics = _workItems.Where(w => w.WorkItemType == "Epic").ToList();
            StateHasChanged();
        }
        else
            Snackbar.Add("Delete failed — check connection.", Severity.Error);
    }
    catch
    {
        Snackbar.Add("Delete failed — check connection.", Severity.Error);
    }
}

private async Task AddWi(string wiType, string parentTitle)
{
    try
    {
        var reqBody = new
        {
            artifactSetId = _artifactSet!.Id,
            workItemType = wiType,
            title = $"New {wiType}",
            parentTitle = parentTitle
        };

        var response = await CallApiAsync(HttpMethod.Post, $"nexus/{Id}/artifacts/wi", reqBody);

        if (response.IsSuccessStatusCode)
        {
            var newWi = await response.Content.ReadFromJsonAsync<WorkItemRecord>();
            if (newWi is not null)
            {
                _workItems.Add(newWi);
                _originalTitles[newWi.Id] = newWi.Title;
                _originalDescriptions[newWi.Id] = newWi.Description ?? "";
                _originalAc[newWi.Id] = newWi.AcceptanceCriteria ?? "";
                _acLists[newWi.Id] = new List<string>();
                if (wiType == "Epic")
                    _epics = _workItems.Where(w => w.WorkItemType == "Epic").ToList();
                StateHasChanged();
            }
        }
        else
            Snackbar.Add("Add failed — check connection.", Severity.Error);
    }
    catch
    {
        Snackbar.Add("Add failed — check connection.", Severity.Error);
    }
}

// ── HTTP Helper (Blazor Server — uses base URL from NavigationManager) ──

private HttpClient? _httpClient;

private HttpClient GetHttpClient()
{
    if (_httpClient is null)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(Nav.BaseUri)
        };
    }
    return _httpClient;
}

private async Task<HttpResponseMessage> CallApiAsync(HttpMethod method, string relativeUrl, object? body)
{
    var http = GetHttpClient();
    var request = new HttpRequestMessage(method, relativeUrl);
    if (body is not null)
    {
        request.Content = JsonContent.Create(body);
    }
    return await http.SendAsync(request);
}
```

## Important Notes for CC

1. **Do NOT use `ParentId` anywhere** — the codebase uses `ParentTitle` (string) exclusively.
2. **The Designer migration file** for the new migration must accurately reflect the snapshot at that migration level. Read the existing Designer file at `src/FortressNexus.Web/Migrations/20260428171338_AddWorkItemRecordDescription.Designer.cs` to get the base snapshot, then add the `acceptance_criteria` column to the `work_item_records` table section.
3. **NexusArtifacts.razor** — do not remove any existing code. Only ADD new markup and @code. The edit-mode markup is conditional (`@if (_editMode) { ... }`) so it doesn't affect the read-only view.
4. **Controller auth** — Use `User.IsInRole(...)` not `UserContextService` (UserContextService uses Blazor's AuthenticationStateProvider which isn't available in controller context).
5. **HTTP calls in Blazor Server** — The `HttpClient` created in the Razor component with `Nav.BaseUri` as base address will work for same-origin API calls. Cookies are NOT forwarded automatically this way. A better pattern: use `IDbContextFactory<NexusDbContext>` directly in the Razor component instead of HTTP calls to the API. However, the spec requires API endpoints. So: create the HttpClient but also consider that cookies in Blazor Server must be forwarded via `IHttpContextAccessor`. For simplicity and correctness, inject `IDbContextFactory<NexusDbContext>` into the Razor component directly and call DB operations inline (bypass the HTTP API for Razor component calls). The 6 API endpoints still need to exist (they're the AC requirement) but the Razor component can use the DbContextFactory directly. DO implement both: the controller endpoints AND direct DbContextFactory calls in the Razor component for the save operations. This is the correct Blazor Server pattern.

Actually, on reflection, let me clarify #5: 

**Use `IDbContextFactory<NexusDbContext>` directly in NexusArtifacts.razor for all save operations.** The controller endpoints still need to be created (they're acceptance criteria), but the Razor component should use the DbContextFactory pattern (same as existing `OnInitializedAsync` which already uses `DbFactory`). This avoids cookie-forwarding complexity entirely.

So:
- Remove the `CallApiAsync` / `GetHttpClient` helper methods from the @code section
- Remove `@inject HttpClient Http` (it's not needed)  
- Remove `@using System.Net.Http` etc.
- Instead, inject `@inject IDbContextFactory<NexusDbContext> DbFactory` (already injected in the existing file!)
- SaveTitle, SaveDescription, SaveAc, MoveWi, ConfirmDelete, AddWi should use `await using var db = await DbFactory.CreateDbContextAsync()` pattern

Here are the revised save method bodies:

```csharp
private async Task SaveTitle(WorkItemRecord wi)
{
    if (string.IsNullOrWhiteSpace(wi.Title))
    {
        wi.Title = _originalTitles.GetValueOrDefault(wi.Id, wi.Title);
        Snackbar.Add("Title cannot be empty — reverted.", Severity.Warning);
        return;
    }
    var oldTitle = _originalTitles.GetValueOrDefault(wi.Id, wi.Title);
    if (oldTitle == wi.Title) return;
    try
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var record = await db.WorkItemRecords.FindAsync(wi.Id);
        if (record is null) { Snackbar.Add("WI not found.", Severity.Error); return; }
        record.Title = wi.Title;
        // Cascade: update ParentTitle refs in same ArtifactSet
        var dependents = await db.WorkItemRecords
            .Where(w => w.ArtifactSetId == wi.ArtifactSetId && w.ParentTitle == oldTitle)
            .ToListAsync();
        foreach (var dep in dependents)
        {
            dep.ParentTitle = wi.Title;
            // Also update local list
            var local = _workItems.FirstOrDefault(w => w.Id == dep.Id);
            if (local is not null) local.ParentTitle = wi.Title;
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        _originalTitles[wi.Id] = wi.Title;
        StateHasChanged();
    }
    catch
    {
        wi.Title = oldTitle;
        Snackbar.Add("Save failed — check connection.", Severity.Error);
    }
}

private async Task SaveDescription(WorkItemRecord wi)
{
    try
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var record = await db.WorkItemRecords.FindAsync(wi.Id);
        if (record is null) return;
        record.Description = wi.Description;
        await db.SaveChangesAsync();
        _originalDescriptions[wi.Id] = wi.Description ?? "";
    }
    catch
    {
        wi.Description = _originalDescriptions.GetValueOrDefault(wi.Id, "");
        Snackbar.Add("Save failed — check connection.", Severity.Error);
    }
}

private async Task SaveAc(WorkItemRecord wi)
{
    if (!_acLists.TryGetValue(wi.Id, out var items)) return;
    var joined = string.Join('\n', items.Where(s => !string.IsNullOrWhiteSpace(s)));
    wi.AcceptanceCriteria = joined;
    try
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var record = await db.WorkItemRecords.FindAsync(wi.Id);
        if (record is null) return;
        record.AcceptanceCriteria = joined;
        await db.SaveChangesAsync();
        _originalAc[wi.Id] = joined;
    }
    catch
    {
        var orig = _originalAc.GetValueOrDefault(wi.Id, "");
        wi.AcceptanceCriteria = orig;
        _acLists[wi.Id] = orig.Split('\n', StringSplitOptions.None)
            .Where(s => !string.IsNullOrEmpty(s)).ToList();
        Snackbar.Add("Save failed — check connection.", Severity.Error);
    }
}

private async Task MoveWi(WorkItemRecord wi, string newParentTitle)
{
    try
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var record = await db.WorkItemRecords.FindAsync(wi.Id);
        if (record is null) return;
        record.ParentTitle = newParentTitle;
        await db.SaveChangesAsync();
        wi.ParentTitle = newParentTitle;
        _epics = _workItems.Where(w => w.WorkItemType == "Epic").ToList();
        StateHasChanged();
    }
    catch
    {
        Snackbar.Add("Move failed — check connection.", Severity.Error);
    }
}

private async Task ConfirmDelete(WorkItemRecord wi)
{
    var descendants = GetAllDescendants(wi);
    string message = descendants.Count > 0
        ? $"Delete '{wi.Title}' and its {descendants.Count} child item(s)? This cannot be undone."
        : $"Delete '{wi.Title}'? This cannot be undone.";
    string yesText = descendants.Count > 0 ? "Delete with children" : "Delete";

    bool? confirmed = await DialogService.ShowMessageBox(
        "Confirm Delete",
        message,
        yesText: yesText,
        cancelText: "Cancel");

    if (confirmed != true) return;

    try
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var allToDelete = new List<int> { wi.Id };
        allToDelete.AddRange(descendants.Select(d => d.Id));
        var dbRecords = await db.WorkItemRecords
            .Where(w => allToDelete.Contains(w.Id))
            .ToListAsync();
        db.WorkItemRecords.RemoveRange(dbRecords);
        await db.SaveChangesAsync();

        _workItems.Remove(wi);
        foreach (var d in descendants) _workItems.Remove(d);
        _epics = _workItems.Where(w => w.WorkItemType == "Epic").ToList();
        StateHasChanged();
    }
    catch
    {
        Snackbar.Add("Delete failed — check connection.", Severity.Error);
    }
}

private async Task AddWi(string wiType, string parentTitle)
{
    try
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var newWi = new WorkItemRecord
        {
            ArtifactSetId = _artifactSet!.Id,
            WorkItemType = wiType,
            Title = $"New {wiType}",
            ParentTitle = parentTitle,
            Status = "Created",
            AdoWorkItemId = 0,
            AdoWorkItemUrl = "",
            WiTemplate = WiTemplateType.Standard
        };
        db.WorkItemRecords.Add(newWi);
        await db.SaveChangesAsync();

        _workItems.Add(newWi);
        _originalTitles[newWi.Id] = newWi.Title;
        _originalDescriptions[newWi.Id] = "";
        _originalAc[newWi.Id] = "";
        _acLists[newWi.Id] = new List<string>();
        if (wiType == "Epic")
            _epics = _workItems.Where(w => w.WorkItemType == "Epic").ToList();
        StateHasChanged();
    }
    catch
    {
        Snackbar.Add("Add failed — check connection.", Severity.Error);
    }
}
```

## Files to Create/Modify

1. **MODIFY** `src/FortressNexus.Web/Models/Entities/WorkItemRecord.cs` — add `AcceptanceCriteria` property
2. **MODIFY** `src/FortressNexus.Web/Data/NexusDbContext.cs` — map `acceptance_criteria` column  
3. **CREATE** `src/FortressNexus.Web/Migrations/20260506000001_AddAcceptanceCriteriaToWorkItemRecord.cs` — migration
4. **CREATE** `src/FortressNexus.Web/Migrations/20260506000001_AddAcceptanceCriteriaToWorkItemRecord.Designer.cs` — snapshot
5. **MODIFY** `src/FortressNexus.Web/Services/UserContextService.cs` — add `IsNexusEditorAsync()`
6. **MODIFY** `src/FortressNexus.Web/Controllers/NexusArtifactsController.cs` — add 6 editor endpoints
7. **MODIFY** `src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor` — add Edit Mode

## Build Verification

After all changes, run:
```bash
cd /home/fredw/projects/fip/nexus && dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj 2>&1 | tail -20
```

If the build succeeds, output "BUILD SUCCEEDED". If it fails, fix the errors and rebuild.

## Output

When complete, output a summary of every file changed and whether the build succeeded.
