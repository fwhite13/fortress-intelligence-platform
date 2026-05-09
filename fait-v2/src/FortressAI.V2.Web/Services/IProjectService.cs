using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public interface IProjectService
{
    // Legacy methods — used by MainLayout (entraOid-based)
    Task<List<ProjectSummary>> GetUserProjectsAsync(string entraOid, CancellationToken ct = default);
    Task<Project> CreateProjectAsync(string entraOid, string name, string? description = null, CancellationToken ct = default);
    Task<string> GetProjectContextAsync(string projectId, CancellationToken ct = default);

    // Full CRUD methods — used by Projects/ProjectDetail/ProjectSettings pages (userId-based)
    Task<List<Project>> GetUserProjectsFullAsync(string userId, CancellationToken ct = default);
    Task<Project?> GetProjectAsync(string projectId, string userId, CancellationToken ct = default);
    Task<Project> CreateProjectFullAsync(string userId, string name, string? description, string? customInstructions, string model = "claude-sonnet-4-6", CancellationToken ct = default);
    Task<Project?> UpdateProjectAsync(string projectId, string userId, string name, string? description, string? customInstructions, string model, bool? enableFortressKb = null, bool? enablePersonalKb = null, CancellationToken ct = default);
    Task<bool> DeleteProjectAsync(string projectId, string userId, CancellationToken ct = default);
}

public class ProjectSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}
