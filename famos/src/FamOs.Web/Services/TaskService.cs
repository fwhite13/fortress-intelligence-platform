using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public class TaskService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;
    private readonly ILogger<TaskService> _logger;

    public TaskService(IDbContextFactory<FamOsDbContext> dbFactory,
        ILogger<TaskService> logger)
    {
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    /// <summary>All open tasks for a user's opportunities — for the Task Center.</summary>
    public async Task<List<TaskWithOpportunity>> GetOpenTasksForUserAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var results = await db.Tasks
            .Include(t => t.Opportunity)
            .Where(t => t.Status == "open"
                && t.Opportunity.OwnerUserId == userId
                && !t.Opportunity.IsClosed)
            .OrderBy(t => t.DueAt.HasValue ? 0 : 1)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();

        return results.Select(t => new TaskWithOpportunity(t, t.Opportunity)).ToList();
    }

    /// <summary>All open tasks across all opportunities (admin view).</summary>
    public async Task<List<TaskWithOpportunity>> GetAllOpenTasksAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var results = await db.Tasks
            .Include(t => t.Opportunity)
            .Where(t => t.Status == "open" && !t.Opportunity.IsClosed)
            .OrderBy(t => t.DueAt.HasValue ? 0 : 1)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
        return results.Select(t => new TaskWithOpportunity(t, t.Opportunity)).ToList();
    }

    /// <summary>Mark a task done.</summary>
    public async Task CompleteTaskAsync(Guid taskId, string actorUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.Tasks.FindAsync(taskId)
            ?? throw new NotFoundException($"Task {taskId} not found");

        task.Status    = "done";
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _logger.LogInformation("[Task] Completed {TaskId} by {User}", taskId, actorUserId);
    }

    /// <summary>Create a manual task on an opportunity.</summary>
    public async Task<Guid> CreateTaskAsync(
        Guid opportunityId, string title, DateTime? dueAt, string? assignedToUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = new FamOsTask
        {
            OpportunityId    = opportunityId,
            Title            = title,
            Status           = "open",
            DueAt            = dueAt,
            AssignedToUserId = assignedToUserId,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    /// <summary>Count of open tasks for a user — for nav badge.</summary>
    public async Task<int> GetOpenTaskCountForUserAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Tasks
            .Where(t => t.Status == "open"
                && t.Opportunity.OwnerUserId == userId
                && !t.Opportunity.IsClosed)
            .CountAsync();
    }
}

public record TaskWithOpportunity(FamOsTask Task, Opportunity Opportunity);
