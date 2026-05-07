using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class ScheduledTaskService : IScheduledTaskService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;

    public ScheduledTaskService(IDbContextFactory<FaitV2DbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<ScheduledTask>> GetUserTasksAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ScheduledTasks
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<ScheduledTask> CreateTaskAsync(string userId, string name, string prompt,
        string scheduleType, string? cronExpression, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var task = new ScheduledTask
        {
            UserId = userId,
            Name = name,
            Prompt = prompt,
            ScheduleType = scheduleType,
            CronExpression = cronExpression,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.ScheduledTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public async Task<ScheduledTask> UpdateTaskAsync(string taskId, string userId, string name, string prompt,
        string? cronExpression, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var task = await db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found for user {userId}.");

        task.Name = name;
        task.Prompt = prompt;
        task.CronExpression = cronExpression;
        task.IsActive = isActive;
        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return task;
    }

    public async Task DeleteTaskAsync(string taskId, string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var task = await db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found for user {userId}.");

        db.ScheduledTasks.Remove(task);
        await db.SaveChangesAsync(ct);
    }

    public async Task TriggerNowAsync(string taskId, string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var task = await db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found for user {userId}.");

        task.NextRunAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ScheduledTaskRun>> GetRunHistoryAsync(string taskId, string userId,
        int limit = 20, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        // Verify task belongs to user before returning runs
        var taskExists = await db.ScheduledTasks
            .AnyAsync(t => t.Id == taskId && t.UserId == userId, ct);
        if (!taskExists)
            throw new InvalidOperationException($"Task {taskId} not found for user {userId}.");

        return await db.ScheduledTaskRuns
            .Where(r => r.TaskId == taskId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
