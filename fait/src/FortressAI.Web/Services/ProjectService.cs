using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

public class ProjectService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ProjectService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<Project>> GetUserProjectsAsync(Guid userId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Projects
            .Where(p => p.UserId == userId)
            .Include(p => p.Documents)
            .Include(p => p.Conversations)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Project?> GetProjectAsync(Guid projectId, Guid userId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Projects
            .Include(p => p.Documents)
            .Include(p => p.Conversations.OrderByDescending(c => c.UpdatedAt))
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);
    }

    public async Task<Project> CreateProjectAsync(Guid userId, string name, string? description, string? customInstructions, string model = "claude-sonnet-4-6")
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var project = new Project
        {
            UserId = userId,
            Name = name.Trim(),
            Description = description?.Trim(),
            CustomInstructions = customInstructions?.Trim(),
            Model = model,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    public async Task<Project?> UpdateProjectAsync(Guid projectId, Guid userId, string name, string? description, string? customInstructions, string model, bool? enableFortressKb = null, bool? enablePersonalKb = null)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);
        if (project == null) return null;

        project.Name = name.Trim();
        project.Description = description?.Trim();
        project.CustomInstructions = customInstructions?.Trim();
        project.Model = model;
        if (enableFortressKb.HasValue) project.EnableFortressKb = enableFortressKb.Value;
        if (enablePersonalKb.HasValue) project.EnablePersonalKb = enablePersonalKb.Value;
        project.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return project;
    }

    public async Task<bool> DeleteProjectAsync(Guid projectId, Guid userId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);
        if (project == null) return false;

        db.Projects.Remove(project);
        await db.SaveChangesAsync();
        return true;
    }
}
