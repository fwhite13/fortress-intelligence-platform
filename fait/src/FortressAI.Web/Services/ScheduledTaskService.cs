using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;
using NCrontab;

namespace FortressAI.Web.Services;

public class ScheduledTaskService : IScheduledTaskService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ScheduledTaskService> _logger;

    public ScheduledTaskService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<ScheduledTaskService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    private static DateTime? CalculateNextRunAt(string? cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression)) return null;
        try
        {
            var schedule = CrontabSchedule.Parse(cronExpression,
                new CrontabSchedule.ParseOptions { IncludingSeconds = false });
            return schedule.GetNextOccurrence(DateTime.UtcNow);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<ScheduledTask>> GetTasksAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ScheduledTasks
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<ScheduledTask?> GetTaskAsync(Guid taskId, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);
    }

    public async Task<ScheduledTask> CreateTaskAsync(Guid userId, CreateScheduledTaskDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var task = new ScheduledTask
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Prompt = dto.Prompt,
            ScheduleType = dto.ScheduleType,
            CronExpression = dto.CronExpression,
            ProjectId = dto.ProjectId,
            AlertOnCompletion = dto.AlertOnCompletion,
            AlertOnFailure = dto.AlertOnFailure,
            TaskMode = dto.TaskMode,
            IsActive = true,
            FailureCount = 0,
            NextRunAt = dto.ScheduleType == "recurring" ? CalculateNextRunAt(dto.CronExpression) : null,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ScheduledTasks.Add(task);
        await db.SaveChangesAsync();
        _logger.LogInformation("Created ScheduledTask {TaskId} for user {UserId}", task.Id, userId);
        return task;
    }

    public async Task<ScheduledTask?> UpdateTaskAsync(Guid taskId, Guid userId, UpdateScheduledTaskDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);
        if (task is null) return null;

        if (dto.Name is not null) task.Name = dto.Name;
        if (dto.Prompt is not null) task.Prompt = dto.Prompt;
        if (dto.ProjectId.HasValue) task.ProjectId = dto.ProjectId;
        if (dto.AlertOnCompletion.HasValue) task.AlertOnCompletion = dto.AlertOnCompletion.Value;
        if (dto.AlertOnFailure.HasValue) task.AlertOnFailure = dto.AlertOnFailure.Value;
        if (dto.TaskMode.HasValue) task.TaskMode = dto.TaskMode.Value;
        if (dto.CronExpression is not null)
        {
            task.CronExpression = dto.CronExpression;
            if (task.ScheduleType == "recurring")
                task.NextRunAt = CalculateNextRunAt(dto.CronExpression);
        }
        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return task;
    }

    public async Task<bool> DeleteTaskAsync(Guid taskId, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);
        if (task is null) return false;

        db.ScheduledTasks.Remove(task);
        await db.SaveChangesAsync();
        _logger.LogInformation("Deleted ScheduledTask {TaskId} for user {UserId}", taskId, userId);
        return true;
    }

    public async Task<bool> PauseAsync(Guid taskId, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);
        if (task is null) return false;

        task.IsActive = false;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResumeAsync(Guid taskId, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);
        if (task is null) return false;

        task.IsActive = true;
        task.NextRunAt = CalculateNextRunAt(task.CronExpression);
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<ScheduledTaskRun>> GetRunHistoryAsync(Guid taskId, Guid userId, int limit = 20)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        // Verify ownership first
        var task = await db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);
        if (task is null) return new List<ScheduledTaskRun>();

        return await db.ScheduledTaskRuns
            .Where(r => r.TaskId == taskId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync();
    }
}
