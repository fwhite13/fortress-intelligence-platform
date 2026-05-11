using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using FortressAI.Web.Data;
using FortressAI.Shared.Models;
using NCrontab;

namespace FortressAI.Web.Services;

public class ScheduledTaskBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledTaskBackgroundService> _logger;
    private readonly int _pollIntervalSeconds;

    public ScheduledTaskBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledTaskBackgroundService> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollIntervalSeconds = config.GetValue<int>("ScheduledTasks:PollIntervalSeconds", 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledTaskBackgroundService starting, poll interval: {Seconds}s", _pollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndDispatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ScheduledTaskBackgroundService poll loop error");
            }
            await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), stoppingToken);
        }
    }

    private async Task PollAndDispatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var dueTasks = await ctx.ScheduledTasks
            .Where(t => t.IsActive && t.NextRunAt != null && t.NextRunAt <= now)
            .ToListAsync(ct);

        foreach (var task in dueTasks)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await ProcessTaskAsync(task, scope.ServiceProvider, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled task {TaskId}", task.Id);
            }
        }
    }

    private async Task ProcessTaskAsync(ScheduledTask task, IServiceProvider services, CancellationToken ct)
    {
        var dbFactory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        // Calculate new next_run_at BEFORE claiming (for recurring tasks)
        var newNextRunAt = task.ScheduleType == "recurring"
            ? CalculateNextRunAt(task.CronExpression)
            : (DateTime?)null;

        // Distributed lock: atomic claim via raw SQL UPDATE.
        // Claim if: NextRunAt is still due AND (LastRunAt is null OR stale > 30 min).
        // This prevents double-dispatch across multiple ECS instances.
        var affected = await ctx.Database.ExecuteSqlRawAsync(
            @"UPDATE scheduled_tasks
              SET LastRunAt = NOW(6),
                  NextRunAt = {0},
                  UpdatedAt = NOW(6)
              WHERE Id = {1}
                AND IsActive = TRUE
                AND NextRunAt IS NOT NULL
                AND NextRunAt <= NOW(6)
                AND (LastRunAt IS NULL OR LastRunAt < DATE_SUB(NOW(6), INTERVAL 30 MINUTE))",
            new object[] { (object?)newNextRunAt ?? DBNull.Value, task.Id.ToString() },
            ct);

        if (affected == 0)
        {
            _logger.LogDebug("Task {TaskId} already claimed by another instance, skipping", task.Id);
            return;
        }

        _logger.LogInformation("Claimed scheduled task {TaskId} ({Name}) for user {UserId}", task.Id, task.Name, task.UserId);

        var runtime = services.GetRequiredService<IUserAgentRuntime>();
        string? resultSummary = null;
        string? errorMessage = null;
        string newStatus;
        var startedAt = DateTime.UtcNow;

        try
        {
            var userId = task.UserId.ToString();

            // ADO#3237 — Resolve user-level enabled MCP server slugs.
            // ScheduledTask has no ConversationId, so we query user-level active servers.
            // Without this, enabledMcpSlugs is [] in the harness, graph_* tools are never
            // added to toolConfig, and the model cannot call MS365/ADO tools.
            var mcpToolSvc = services.GetRequiredService<IMcpToolService>();
            var activeServers = await mcpToolSvc.GetActiveServersForUserAsync(task.UserId);
            var enabledMcpSlugs = activeServers
                .Select(s => s.Slug)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToList();

            var turnRequest = new TurnRequest(
                UserId: userId,
                Message: task.Prompt,
                IsScheduledTask: true,
                TaskMode: task.TaskMode,
                EnabledMcpSlugs: enabledMcpSlugs.Count > 0 ? enabledMcpSlugs : null
            );

            var sb = new System.Text.StringBuilder();
            await foreach (var evt in runtime.SendTurnAsync(userId, turnRequest, ct))
            {
                if (evt.Type == "text" && !string.IsNullOrEmpty(evt.Content))
                    sb.Append(evt.Content);
            }
            resultSummary = sb.Length > 500 ? sb.ToString(0, 500) : sb.ToString();
            newStatus = "success";
            _logger.LogInformation("Task {TaskId} completed successfully", task.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Task {TaskId} dispatch failed", task.Id);
            newStatus = "failed";
            errorMessage = ex.Message;
        }

        // Write run row with final status — no intermediate "running" row
        var run = new ScheduledTaskRun
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Status = newStatus,
            ResultSummary = newStatus == "success" ? resultSummary : null,
            Error = newStatus == "failed" ? errorMessage : null
        };
        ctx.ScheduledTaskRuns.Add(run);

        var taskToUpdate = await ctx.ScheduledTasks.FindAsync(new object[] { task.Id }, ct);
        if (taskToUpdate != null)
        {
            taskToUpdate.UpdatedAt = DateTime.UtcNow;
            if (newStatus == "success")
            {
                taskToUpdate.LastRunStatus = "success";
                taskToUpdate.FailureCount = 0;
                // next_run_at already set by the lock UPDATE for recurring;
                // for on_demand tasks, clear it so they don't re-fire
                if (task.ScheduleType != "recurring")
                    taskToUpdate.NextRunAt = null;
            }
            else
            {
                taskToUpdate.FailureCount++;
                taskToUpdate.LastRunStatus = "failed";

                if (taskToUpdate.FailureCount == 1)
                {
                    // First failure: retry in 5 minutes
                    taskToUpdate.NextRunAt = DateTime.UtcNow.AddMinutes(5);
                    _logger.LogWarning("Task {TaskId} failed (count=1), retrying in 5 min", task.Id);
                }
                else
                {
                    // failure_count >= 2: permanently failed, deactivate
                    taskToUpdate.NextRunAt = null;
                    taskToUpdate.IsActive = false;
                    _logger.LogWarning("Task {TaskId} failed permanently (count={Count}), deactivated", task.Id, taskToUpdate.FailureCount);
                }
            }
        }

        await ctx.SaveChangesAsync(ct);

        // Dual-channel notifications (SignalR + MS365 email) — fire-and-forget, best-effort, fires AFTER DB write
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = services.CreateScope();
                var notifySvc = scope.ServiceProvider.GetRequiredService<ITaskNotificationService>();
                if (newStatus == "success" && task.AlertOnCompletion)
                {
                    await notifySvc.NotifyTaskCompletedAsync(task.UserId, task.Name, resultSummary);
                }
                else if (newStatus == "failed" && taskToUpdate?.FailureCount >= 2 && task.AlertOnFailure)
                {
                    await notifySvc.NotifyTaskPermanentlyFailedAsync(task.UserId, task.Name, errorMessage ?? "Unknown error");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Task notification failed for task {TaskId} — task status unaffected", task.Id);
            }
        }, CancellationToken.None);
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
}
