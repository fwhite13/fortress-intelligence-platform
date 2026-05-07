# BUILD Brief: ADO#2861 — FAIT v2: Projects carry-over from FAIT v1 - bidirectional read

**ADO WI:** #2861 (Fortress project)
**Repo:** `/home/fredw/projects/fip`
**Service:** `fait-v2/src/FortressAI.V2.Web/`
**Sprint:** FAIT v2 Sprint 4 — FAIT v1 Continuity

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2861-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/fait-v2/`

---

## Context

FAIT v2 and FAIT v1 share the same Aurora MySQL database (the "FIP Portal DB"). FAIT v2 has `FipPortalDbContext.cs` already wired. The `Project` model exists at `Data/Models/Project.cs`. This WI surfaces existing FAIT v1 projects in the FAIT v2 left sidebar and injects project context into assistant turns when a project is selected.

**Current state:**
- `Data/FipPortalDbContext.cs` — exists (shared Aurora DB context)
- `Data/Models/Project.cs` — exists
- `Components/Pages/Dashboard.razor` — exists (main page where sidebar lives)
- No project list or selection UI in sidebar yet

**DB connection:** `GuidFormat=MySqlGuidFormat.None` on ALL connections (mandatory — in place from Sprint 2).

---

## Implementation

### 1. Check `Project.cs` model and ensure it matches FAIT v1 schema

Look at `Data/Models/Project.cs`. It should map to the `projects` table in the shared Aurora DB. Ensure:
- GUID fields use `string` type (varchar(36), GuidFormat=None)
- Key fields: `Id`, `Name`, `Description`, `OwnerId`, `TeamId`, `CreatedAt`, `IsArchived`
- If `IsArchived` column doesn't exist in model, add it (nullable bool)

### 2. Add `IProjectService` interface
File: `Services/IProjectService.cs`

```csharp
public interface IProjectService
{
    Task<List<ProjectSummary>> GetUserProjectsAsync(string userId, CancellationToken ct = default);
    Task<Project?> GetProjectAsync(string projectId, CancellationToken ct = default);
    Task<Project> CreateProjectAsync(string userId, string name, string? description = null, CancellationToken ct = default);
    Task<string> GetProjectContextAsync(string projectId, CancellationToken ct = default);
}

public class ProjectSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 3. Implement `ProjectService`
File: `Services/ProjectService.cs`

Uses `FipPortalDbContext` to query the shared Aurora DB:

```csharp
public class ProjectService : IProjectService
{
    private readonly FipPortalDbContext _db;
    
    public async Task<List<ProjectSummary>> GetUserProjectsAsync(string userId, CancellationToken ct = default)
    {
        // Get projects where user is owner or team member
        // Simple implementation: query projects where OwnerId = userId OR 
        // user is in the project's team
        return await _db.Projects
            .Where(p => !p.IsArchived && (p.OwnerId == userId || 
                _db.ProjectMembers.Any(pm => pm.ProjectId == p.Id && pm.UserId == userId)))
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new ProjectSummary 
            { 
                Id = p.Id, 
                Name = p.Name,
                Description = p.Description,
                UpdatedAt = p.UpdatedAt,
            })
            .Take(50)
            .ToListAsync(ct);
    }
    
    public async Task<string> GetProjectContextAsync(string projectId, CancellationToken ct = default)
    {
        var project = await _db.Projects.FindAsync([projectId], ct);
        if (project == null) return string.Empty;
        
        return $"# Active Project: {project.Name}\n{project.Description ?? ""}";
    }
    
    // ... CreateProjectAsync implementation
}
```

**Note:** If `ProjectMembers` table/model doesn't exist in the shared DB schema, fall back to just querying by `OwnerId`. Check the existing DB context to understand what's there.

### 4. Wire project list into sidebar

In `Dashboard.razor`, add a projects section to the left sidebar:

```razor
<!-- In the sidebar section, after recent conversations -->
<div class="sidebar-section">
    <div class="sidebar-section-header">
        <span>Projects</span>
        <MudIconButton Icon="@Icons.Material.Outlined.Add" Size="Size.Small" 
                       OnClick="OpenNewProjectDialog" />
    </div>
    @if (_projects.Count == 0)
    {
        <div class="sidebar-empty">No projects yet</div>
    }
    else
    {
        @foreach (var project in _projects)
        {
            <div class="sidebar-item @(SelectedProjectId == project.Id ? "active" : "")"
                 @onclick="() => SelectProject(project.Id)">
                <MudIcon Icon="@Icons.Material.Outlined.FolderOpen" Size="Size.Small" />
                <span>@project.Name</span>
            </div>
        }
    }
</div>
```

In the `@code` block:
```csharp
private List<ProjectSummary> _projects = new();
private string? SelectedProjectId;

protected override async Task OnInitializedAsync()
{
    // ... existing init ...
    _projects = await ProjectService.GetUserProjectsAsync(CurrentUser.Id);
}

private async Task SelectProject(string projectId)
{
    SelectedProjectId = projectId;
    // Store in session state so assistant picks it up
    _activeProjectContext = await ProjectService.GetProjectContextAsync(projectId);
    StateHasChanged();
}
```

### 5. Inject project context into assistant turns

When `_activeProjectContext` is set, prepend it to the user's message before sending to Bedrock:

```csharp
private string BuildContextualPrompt(string userMessage)
{
    if (string.IsNullOrEmpty(_activeProjectContext))
        return userMessage;
    
    return $"[ACTIVE PROJECT CONTEXT]\n{_activeProjectContext}\n\n[USER MESSAGE]\n{userMessage}";
}
```

### 6. Register service in `Program.cs`

```csharp
builder.Services.AddScoped<IProjectService, ProjectService>();
```

---

## Constraints

- **Entra auth only** — no Cognito
- **GuidFormat=MySqlGuidFormat.None** on ALL Aurora connections (mandatory)
- **varchar(36)** for GUID columns — use `string` type in C# models, not `Guid`
- **CSS variables only** — no hardcoded colors/fonts/sizes in Razor or inline styles
- Bidirectional: projects created in v2 use same `projects` table → visible in v1

---

## Acceptance Criteria

- [ ] `IProjectService` + `ProjectService` implemented using `FipPortalDbContext`
- [ ] Existing FAIT v1 projects visible in FAIT v2 left sidebar (filtered to authenticated user's projects)
- [ ] Selecting a project injects project context into the assistant turn prompt
- [ ] New projects created in v2 use the same `projects` table (visible in v1)
- [ ] Team memberships respected — users only see their own projects
- [ ] GuidFormat=None on all Aurora connections
- [ ] `dotnet build` succeeds
- [ ] All CSS via variables, no hardcoded values

---

## ADO Tracking (MANDATORY)

After build complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2861,
  "text": "**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED."
}'
```

---

## Deliverables

1. `Services/IProjectService.cs` (new)
2. `Services/ProjectService.cs` (new)
3. `Components/Pages/Dashboard.razor` (updated — project sidebar)
4. `Program.cs` (updated — service registration)
5. Build Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2861-BUILD-REPORT.md`
