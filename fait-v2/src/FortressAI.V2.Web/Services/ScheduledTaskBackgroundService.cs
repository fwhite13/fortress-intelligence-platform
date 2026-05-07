using Cronos;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class ScheduledTaskBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly IServiceProvider _services;
    private readonly ILogger<ScheduledTaskBackgroundService> _logger;

    public ScheduledTaskBackgroundService(
        IDbContextFactory<FaitV2DbContext> dbFactory,
        IServiceProvider services,
        ILogger<ScheduledTaskBackgroundService> logger)
    {
        _dbFactory = dbFactory;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledTaskBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in scheduled task poll cycle.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var dueTasks = await db.ScheduledTasks
            .Where(t => t.IsActive
                && t.NextRunAt != null
                && t.NextRunAt <= now
                && (t.LastRunStatus != "running"
                    || t.LastRunAt < now.AddMinutes(-30)))
            .ToListAsync(ct);

        if (dueTasks.Count == 0) return;

        _logger.LogInformation("Found {Count} due scheduled task(s).", dueTasks.Count);

        foreach (var task in dueTasks)
        {
            await ProcessTaskAsync(task, ct);
        }
    }

    private async Task ProcessTaskAsync(ScheduledTask task, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Distributed lock via compare-and-swap UPDATE
        var claimed = await db.Database.ExecuteSqlRawAsync(
            @"UPDATE scheduled_tasks
              SET last_run_status = 'running', last_run_at = UTC_TIMESTAMP(6)
              WHERE id = {0} AND (last_run_status != 'running' OR last_run_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 30 MINUTE))",
            task.Id);

        if (claimed == 0)
        {
            _logger.LogDebug("Task {TaskId} already claimed by another instance, skipping.", task.Id);
            return;
        }

        var run = new ScheduledTaskRun
        {
            TaskId = task.Id,
            StartedAt = DateTime.UtcNow,
            Status = "running",
        };
        db.ScheduledTaskRuns.Add(run);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Dispatching scheduled task {TaskId} (run {RunId}).", task.Id, run.Id);

        try
        {
            using var scope = _services.CreateScope();
            var ccService = scope.ServiceProvider.GetRequiredService<ICCExecutionService>();

            var envelope = new CCContextEnvelope
            {
                UserId = task.UserId,
                UserDisplayName = string.Empty,
                TaskInstructions = task.Prompt,
            };

            var result = await ccService.DispatchTaskAsync(
                task.UserId,
                task.Prompt,
                envelope,
                cancellationToken: ct);

            // Update run record
            run.Status = result.Success ? "success" : "failed";
            run.CompletedAt = DateTime.UtcNow;
            run.ArtifactS3Key = result.ArtifactS3Key;
            if (!result.Success)
                run.ErrorMessage = result.Error;

            // Reload task to update against latest DB state
            var dbTask = await db.ScheduledTasks.FindAsync(new object[] { task.Id }, ct);
            if (dbTask != null)
            {
                if (result.Success)
                {
                    dbTask.LastRunStatus = "success";
                    dbTask.FailureCount = 0;
                    dbTask.NextRunAt = ComputeNextRun(dbTask);
                }
                else
                {
                    dbTask.LastRunStatus = "failed";
                    dbTask.FailureCount += 1;

                    if (dbTask.FailureCount == 1)
                    {
                        dbTask.NextRunAt = DateTime.UtcNow.AddMinutes(5);
                        _logger.LogWarning("Task {TaskId} failed; scheduling retry in 5 minutes.", task.Id);
                    }
                    else
                    {
                        dbTask.IsActive = false;
                        dbTask.NextRunAt = null;
                        _logger.LogWarning("Task {TaskId} failed twice; deactivating.", task.Id);
                    }
                }
                dbTask.LastRunAt = DateTime.UtcNow;
                dbTask.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while dispatching scheduled task {TaskId}.", task.Id);

            run.Status = "failed";
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorMessage = ex.Message;

            var dbTask = await db.ScheduledTasks.FindAsync(new object[] { task.Id }, ct);
            if (dbTask != null)
            {
                dbTask.LastRunStatus = "failed";
                dbTask.LastRunAt = DateTime.UtcNow;
                dbTask.UpdatedAt = DateTime.UtcNow;
                dbTask.FailureCount += 1;

                if (dbTask.FailureCount == 1)
                {
                    dbTask.NextRunAt = DateTime.UtcNow.AddMinutes(5);
                }
                else
                {
                    dbTask.IsActive = false;
                    dbTask.NextRunAt = null;
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static DateTime? ComputeNextRun(ScheduledTask task)
    {
        if (task.ScheduleType != "recurring" || string.IsNullOrEmpty(task.CronExpression))
            return null;

        try
        {
            var cron = CronExpression.Parse(task.CronExpression, CronFormat.Standard);
            return cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
