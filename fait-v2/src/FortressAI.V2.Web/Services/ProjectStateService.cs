namespace FortressAI.V2.Web.Services;

/// <summary>
/// Scoped service that carries the active project context across Blazor components.
/// Set by MainLayout when user selects a project; read by ChatView when building prompts.
/// </summary>
public class ProjectStateService
{
    public string? ActiveProjectId { get; private set; }
    public string ActiveProjectContext { get; private set; } = string.Empty;

    public void SetActiveProject(string projectId, string context)
    {
        ActiveProjectId = projectId;
        ActiveProjectContext = context;
    }

    public void ClearActiveProject()
    {
        ActiveProjectId = null;
        ActiveProjectContext = string.Empty;
    }
}
