# BUILD Assignment: ADO#2500

## Task
**NexusArtifacts UI — Test Case grouping, WI template badges, predecessor badges, external dependency panel**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2500

## MANDATORY: Read the spec first
Read the full spec before touching any markup:
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Focus on **§8 — UI Specification** — it has wireframes, exact MudBlazor components, badge colors, copy button behavior, and panel layout.

## Repo
`/home/fredw/projects/fip/nexus/`
Working directory: `src/FortressNexus.Web/`

## Step 0: Reconnaissance — read existing code first
Before writing any markup, read:
1. `Components/Pages/NexusArtifacts.razor` — understand the current tree structure, how Epics/Features/Stories/Tasks are rendered, what data is already loaded, what models are used
2. `Controllers/NexusArtifactsController.cs` — understand existing endpoints and auth policy to match
3. Understand how `ArtifactSet` and `WorkItemRecord` collections are passed to the page

All new fields are already on `WorkItemRecord`: `WiTemplate`, `IsExternalDependency`, `ExternalOwner`, `TestedByTitles`, `PredecessorTitles`, `ParentTitle`, `ExternalDependencyCount` on `ArtifactSet`.

## What to build

### 1. External Dependencies Panel (above WI tree)
Add before the main WI tree, rendered only when `ArtifactSet.ExternalDependencyCount > 0`:

```razor
@if (ArtifactSet.ExternalDependencyCount > 0)
{
    <MudAlert Severity="Severity.Warning" Class="mb-4">
        ⚠️ @ArtifactSet.ExternalDependencyCount external dependencies require action before these WIs can be completed
    </MudAlert>

    <MudExpansionPanel Text="External Dependencies" IsInitiallyExpanded="true">
        @foreach (var extWi in ExternalDependencies)
        {
            <MudCard Class="mb-2">
                <MudCardContent>
                    <MudText Typo="Typo.subtitle2"><strong>@extWi.ExternalOwner</strong></MudText>
                    <MudText Typo="Typo.body1">@extWi.Title</MudText>
                    <MudText Typo="Typo.body2">@(extWi.Description?.Length > 120 ? extWi.Description[..120] + "…" : extWi.Description)</MudText>
                    @foreach (var tag in extWi.Tags ?? [])
                    {
                        <MudChip Size="Size.Small">@tag</MudChip>
                    }
                    <MudButton OnClick="@(() => CopyToClipboard(extWi.Description))" StartIcon="@Icons.Material.Outlined.ContentCopy" Size="Size.Small">Copy brief</MudButton>
                </MudCardContent>
            </MudCard>
        }
    </MudExpansionPanel>
}
```

For `ExternalDependencies`: either fetch from the new endpoint `GET /nexus/{id}/artifacts/external-dependencies` on page load, OR filter from the already-loaded WI list (`WorkItems.Where(w => w.IsExternalDependency)`). The simpler approach (filter from loaded list) is fine for Phase 1.

Copy brief JS interop:
```csharp
@inject IJSRuntime JS

private async Task CopyToClipboard(string? text)
{
    if (text is not null)
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);
}
```

### 2. WI Template Badges
In the WI row rendering (wherever the WI type badge is currently shown), add a template badge to the left:

```razor
@if (wi.WiTemplate == WiTemplateType.Infrastructure)
{
    <MudChip Color="Color.Info" Size="Size.Small" Class="mr-1">🏗️</MudChip>
}
else if (wi.WiTemplate == WiTemplateType.Migration)
{
    <MudChip Color="Color.Secondary" Size="Size.Small" Class="mr-1">🔄</MudChip>
}
else if (wi.WiTemplate == WiTemplateType.TestCase)
{
    <MudChip Color="Color.Primary" Size="Size.Small" Class="mr-1">🧪</MudChip>
}
```

Colors: Infrastructure = teal (use `Color.Info` or a teal style), Migration = purple (`Color.Secondary`), TestCase = blue (`Color.Primary`). Standard WIs show no badge.

### 3. Predecessor Badges
After the WI title in each row, render predecessor badges inline:

```razor
@if (wi.PredecessorTitles?.Any() == true)
{
    @foreach (var predTitle in wi.PredecessorTitles)
    {
        var isCrossEpic = IsCrossEpicPredecessor(wi, predTitle);
        var isUnresolved = IsUnresolvedPredecessor(predTitle);

        if (isUnresolved)
        {
            <MudTooltip Text="Could not be auto-linked">
                <MudChip Color="Color.Error" Size="Size.Small">⛓ [!] @Truncate(predTitle, 30)</MudChip>
            </MudTooltip>
        }
        else if (isCrossEpic)
        {
            <MudTooltip Text="@predTitle">
                <MudChip Color="Color.Warning" Size="Size.Small">⛓ Cross-Epic: @Truncate(predTitle, 30)</MudChip>
            </MudTooltip>
        }
        else
        {
            <MudTooltip Text="@predTitle">
                <MudChip Color="Color.Warning" Size="Size.Small">⛓ Blocked by: @Truncate(predTitle, 30)</MudChip>
            </MudTooltip>
        }
    }
}
```

For determining cross-Epic vs same-Epic: check if any WI in the current batch has a different parent Epic than the current WI but matches the predecessor title. A simple heuristic: if the predecessor title's WI is under a different top-level Epic in `WorkItems`, it's cross-Epic. Implement a helper method.

Unresolved: a predecessor title that doesn't match any WI title in the current `WorkItems` list.

### 4. Test Case Collapsible Subsection
Within the User Story node rendering (after Tasks), add:

```razor
@{
    var testCases = WorkItems.Where(w => w.WiType == "Test Case" && w.ParentTitle == story.Title).ToList();
}
@if (testCases.Any())
{
    <MudExpansionPanel Text=@($"🧪 Test Cases ({testCases.Count})") IsInitiallyExpanded="false" Class="mt-2">
        @foreach (var tc in testCases)
        {
            <MudText Typo="Typo.body2" Class="ml-4">
                <MudChip Color="Color.Primary" Size="Size.Small">🧪</MudChip>
                @tc.Title
            </MudText>
            <MudText Typo="Typo.body2" Class="ml-8">@tc.AcceptanceCriteria</MudText>
        }
    </MudExpansionPanel>
}
```

Test Cases must NOT appear in the flat tree — filter them out of the main Epic→Feature→Story→Task rendering. Check if Test Case WIs are currently being rendered inline and exclude them.

### 5. GET /nexus/{id}/artifacts/external-dependencies endpoint
In `Controllers/NexusArtifactsController.cs`, add:

```csharp
[HttpGet("{id}/artifacts/external-dependencies")]
[Authorize(Policy = "<same-policy-as-existing-artifacts-route>")]
public async Task<IActionResult> GetExternalDependencies(Guid id)
{
    var artifactSet = await _db.ArtifactSets
        .Where(a => a.SubmissionId == id)
        .OrderByDescending(a => a.CreatedAt)
        .FirstOrDefaultAsync();

    if (artifactSet == null) return NotFound();

    var externalWis = await _db.WorkItemRecords
        .Where(w => w.ArtifactSetId == artifactSet.Id && w.IsExternalDependency)
        .ToListAsync();

    return Ok(externalWis);
}
```

Check the existing controller for the exact auth policy name, navigation property names, and EF query pattern — match them exactly.

## ⚠️ StubAdoService reminder
If this WI requires any new field mappings in StubAdoService, add them to BOTH `CreateWorkItemAsync` AND `CreateWorkItemBatchAsync`. This WI is UI-only and shouldn't need StubAdoService changes, but if you touch it for any reason, follow the rule.

## MudBlazor version note
Check `FortressNexus.Web.csproj` for the MudBlazor version in use. Some component APIs differ between v6 and v7. Use the existing components in the file as a reference for correct API usage. `Icons.Material.Outlined.OutboxRounded` does NOT exist in MudBlazor v7 — use `Icons.Material.Outlined.Send` or `Icons.Material.Outlined.ContentCopy` instead.

## ADO Updates (MANDATORY)
After implementing, add a comment to ADO WI #2500:
```
mcporter call devops.add_comment project="FAIT" id=2500 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {summary}. Build: SUCCEEDED."
```

## Build Report required
Create `/home/fredw/projects/fip/nexus/pipeline/ADO2500-BUILD-REPORT.md` with:
- Files modified (with full paths)
- Commit hash
- Build result (`dotnet build` output)
- CC invocation command used
- Self-review checklist: all 12 AC items verified
- Note on how ExternalDependencies list is populated (endpoint fetch vs filter from loaded list)

## Notify Maria when done
When completely finished, run:
openclaw system event --text "ADO2500 BUILD COMPLETE: NexusArtifacts UI upgrade" --mode now
