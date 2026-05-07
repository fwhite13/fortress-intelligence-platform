# BUILD Brief C2: ADO#2861 — Projects carry-over fixes

**ADO WI:** #2861 (Fortress project)
**Review Cycle:** 1 → NEEDS-CHANGES
**Prior Commit:** `2681804`

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2861-BUILD-BRIEF-C2.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/fait-v2/`

---

## Fix Only These Two Issues — No Scope Creep

### Fix 1: Wire `ProjectStateService.ActiveProjectContext` to a consumer

`ProjectStateService` is set in `MainLayout.razor:SelectProjectAsync` but nothing reads it. The simplest fix that satisfies the acceptance criteria:

**In `Dashboard.razor`** (the main chat page), inject `ProjectStateService` and when `ActiveProjectContext` is non-empty, prepend it to the user message before sending to Bedrock. Look at how the existing `BedrockService` call is made in `Dashboard.razor` and add:

```csharp
@inject ProjectStateService ProjectState

// In the method that sends a message to Bedrock:
var messageToSend = string.IsNullOrEmpty(ProjectState.ActiveProjectContext)
    ? userInput
    : $"[ACTIVE PROJECT CONTEXT]\n{ProjectState.ActiveProjectContext}\n\n[USER MESSAGE]\n{userInput}";
```

If `Dashboard.razor` doesn't have a direct Bedrock call yet (it may delegate to a service), add a `BuildContextualMessage(string userInput)` helper that does the prepending, and call it at the point where the message is sent.

**Minimum acceptable:** The context must flow from `ProjectStateService` into the assistant's prompt in at least one code path. No orphaned state.

### Fix 2: Implement `OpenNewProjectDialog`

In `MainLayout.razor`, replace the empty `OpenNewProjectDialog()` stub with a real `MudDialog`:

```razor
<!-- Add dialog markup near bottom of component, before @code -->
<MudDialog @bind-IsVisible="_showNewProjectDialog">
    <TitleContent>New Project</TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_newProjectName" Label="Project name" Required="true" />
        <MudTextField @bind-Value="_newProjectDescription" Label="Description (optional)" Lines="2" Class="mt-2" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="() => _showNewProjectDialog = false">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   Disabled="@string.IsNullOrWhiteSpace(_newProjectName)"
                   OnClick="CreateProject">Create</MudButton>
    </DialogActions>
</MudDialog>
```

In `@code`:
```csharp
private bool _showNewProjectDialog;
private string _newProjectName = string.Empty;
private string? _newProjectDescription;

private void OpenNewProjectDialog()
{
    _newProjectName = string.Empty;
    _newProjectDescription = null;
    _showNewProjectDialog = true;
}

private async Task CreateProject()
{
    if (string.IsNullOrWhiteSpace(_newProjectName)) return;
    var project = await ProjectService.CreateProjectAsync(_entraOid, _newProjectName, _newProjectDescription);
    _projects.Add(new ProjectSummary
    {
        Id = project.Id,
        Name = project.Name,
        Description = project.Description,
        UpdatedAt = project.UpdatedAt,
    });
    _showNewProjectDialog = false;
    await SelectProjectAsync(_projects.Last());
    StateHasChanged();
}
```

---

## Constraints

- CSS variables only — no hardcoded values
- No Cognito references
- `dotnet build` must pass 0 errors

---

## ADO Tracking (MANDATORY)

After fix complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2861,
  "text": "**[Tony Stark — BUILD cycle 2]**\nCommit {hash}: Fix ProjectStateService consumer wiring + OpenNewProjectDialog implementation. Build: SUCCEEDED."
}'
```

---

## Deliverables

1. `Components/Pages/Dashboard.razor` — `ProjectState.ActiveProjectContext` consumed in message send path
2. `Components/Layout/MainLayout.razor` — `OpenNewProjectDialog` implemented with `MudDialog`
3. Build Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2861-BUILD-REPORT-C2.md`
