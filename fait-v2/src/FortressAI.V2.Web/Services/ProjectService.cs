using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class ProjectService : IProjectService
{
    private readonly FaitV2DbContext _db;

    public ProjectService(FaitV2DbContext db) => _db = db;

    public async Task<List<ProjectSummary>> GetUserProjectsAsync(string entraOid, CancellationToken ct = default)
    {
        return await _db.Projects
            .Where(p => p.User!.EntraOid == entraOid)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new ProjectSummary
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                UpdatedAt = p.UpdatedAt
            })
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<Project?> GetProjectAsync(string projectId, CancellationToken ct = default)
        => await _db.Projects.FindAsync([projectId], ct);

    public async Task<Project> CreateProjectAsync(string entraOid, string name, string? description = null, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstAsync(u => u.EntraOid == entraOid, ct);
        var project = new Project
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<string> GetProjectContextAsync(string projectId, CancellationToken ct = default)
    {
        var project = await _db.Projects.FindAsync([projectId], ct);
        if (project == null) return string.Empty;
        return $"# Active Project: {project.Name}\n{project.Description ?? ""}".Trim();
    }
}
