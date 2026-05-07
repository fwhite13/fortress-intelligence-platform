using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public interface IProjectService
{
    Task<List<ProjectSummary>> GetUserProjectsAsync(string entraOid, CancellationToken ct = default);
    Task<Project?> GetProjectAsync(string projectId, CancellationToken ct = default);
    Task<Project> CreateProjectAsync(string entraOid, string name, string? description = null, CancellationToken ct = default);
    Task<string> GetProjectContextAsync(string projectId, CancellationToken ct = default);
}

public class ProjectSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}
