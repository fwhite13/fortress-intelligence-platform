using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class ProjectService : IProjectService
{
    private readonly IDbContextFactory<FaitV2DbContext> _contextFactory;

    public ProjectService(IDbContextFactory<FaitV2DbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // --- Legacy methods (entraOid-based) used by MainLayout ---

    public async Task<List<ProjectSummary>> GetUserProjectsAsync(string entraOid, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Projects
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

    public async Task<Project> CreateProjectAsync(string entraOid, string name, string? description = null, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstAsync(u => u.EntraOid == entraOid, ct);
        var project = new Project
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Name = name,
            Description = description,
            Model = "claude-sonnet-4-6",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<string> GetProjectContextAsync(string projectId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.FindAsync([projectId], ct);
        if (project == null) return string.Empty;
        return $"# Active Project: {project.Name}\n{project.Description ?? ""}".Trim();
    }

    // --- Full CRUD methods (userId-based) used by new project pages ---

    public async Task<List<Project>> GetUserProjectsFullAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Projects
            .Where(p => p.UserId == userId)
            .Include(p => p.Documents)
            .Include(p => p.ConversationTasks)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<Project?> GetProjectAsync(string projectId, string userId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Projects
            .Include(p => p.Documents)
            .Include(p => p.ConversationTasks.OrderByDescending(c => c.UpdatedAt))
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);
    }

    public async Task<Project> CreateProjectFullAsync(string userId, string name, string? description, string? customInstructions, string model = "claude-sonnet-4-6", CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var project = new Project
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Name = name.Trim(),
            Description = description?.Trim(),
            CustomInstructions = customInstructions?.Trim(),
            Model = model,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<Project?> UpdateProjectAsync(string projectId, string userId, string name, string? description, string? customInstructions, string model, bool? enableFortressKb = null, bool? enablePersonalKb = null, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);
        if (project == null) return null;

        project.Name = name.Trim();
        project.Description = description?.Trim();
        project.CustomInstructions = customInstructions?.Trim();
        project.Model = model;
        if (enableFortressKb.HasValue) project.EnableFortressKb = enableFortressKb.Value;
        if (enablePersonalKb.HasValue) project.EnablePersonalKb = enablePersonalKb.Value;
        project.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<bool> DeleteProjectAsync(string projectId, string userId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);
        if (project == null) return false;

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
