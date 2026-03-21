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
                && (
                    (t.OpportunityId != null && t.Opportunity.OwnerUserId != null && t.Opportunity.OwnerUserId == userId && !t.Opportunity.IsClosed)
                    || (t.OpportunityId == null && t.AssignedToUserId == userId)
                ))
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
            .Where(t => t.Status == "open"
                && (t.OpportunityId == null || !t.Opportunity!.IsClosed))
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

    /// <summary>Create a manual task, optionally linked to an opportunity.</summary>
    public async Task<Guid> CreateTaskAsync(
        Guid? opportunityId, string title, DateTime? dueAt, string? assignedToUserId)
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

    public const int TaskPageSize = 25;

    /// <summary>Returns one page of open tasks for a user, sorted by due-date urgency.</summary>
    public async Task<TaskPage> GetOpenTasksPagedAsync(string userId, int pageIndex)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.Tasks
            .Include(t => t.Opportunity)
            .Where(t => t.Status == "open"
                && (
                    (t.OpportunityId != null && t.Opportunity.OwnerUserId != null && t.Opportunity.OwnerUserId == userId && !t.Opportunity.IsClosed)
                    || (t.OpportunityId == null && t.AssignedToUserId == userId)
                ));

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(t => t.DueAt.HasValue ? 0 : 1)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.CreatedAt)
            .Skip(pageIndex * TaskPageSize)
            .Take(TaskPageSize)
            .Select(t => new TaskWithOpportunity(t, t.Opportunity))
            .ToListAsync();

        return new TaskPage
        {
            Items      = items,
            TotalCount = total,
            PageIndex  = pageIndex,
            PageSize   = TaskPageSize,
            HasMore    = (pageIndex + 1) * TaskPageSize < total,
        };
    }

    /// <summary>Count of open tasks for a user — for nav badge.</summary>
    public async Task<int> GetOpenTaskCountForUserAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Tasks
            .Where(t => t.Status == "open"
                && (
                    (t.OpportunityId != null && t.Opportunity.OwnerUserId != null && t.Opportunity.OwnerUserId == userId && !t.Opportunity.IsClosed)
                    || (t.OpportunityId == null && t.AssignedToUserId == userId)
                ))
            .CountAsync();
    }
}

public record TaskWithOpportunity(FamOsTask Task, Opportunity? Opportunity);

public class TaskPage
{
    public List<TaskWithOpportunity> Items { get; init; } = new();
    public int  TotalCount { get; init; }
    public int  PageIndex  { get; init; }
    public int  PageSize   { get; init; }
    public bool HasMore    { get; init; }
}
