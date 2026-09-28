using Amazon.ECS;
using Amazon.ECS.Model;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// WI #7691: recovers meetings stuck in Pending/Joining because the vpbot ECS task died before
/// it could post a status callback. Every 5 minutes, any Pending/Joining meeting untouched for
/// 10+ minutes has its bot task checked; if the task is STOPPED or no longer known to ECS, the
/// meeting is reverted to Scheduled with LastFailureReason = "bot_launch_failed".
/// Tasks still provisioning/running are left alone (legitimate slow start or lobby wait).
/// </summary>
public class StuckMeetingRecoveryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
    private const string FailureReason = "bot_launch_failed";

    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IAmazonECS _ecs;
    private readonly IConfiguration _config;
    private readonly ILogger<StuckMeetingRecoveryService> _logger;

    public StuckMeetingRecoveryService(
        IDbContextFactory<FirmDbContext> dbFactory,
        IAmazonECS ecs,
        IConfiguration config,
        ILogger<StuckMeetingRecoveryService> logger)
    {
        _dbFactory = dbFactory;
        _ecs = ecs;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FIRM: StuckMeetingRecoveryService started. Interval: {Interval}, stale after: {Stale}", Interval, StaleAfter);

        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    await RecoverStuckMeetingsAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "FIRM: StuckMeetingRecoveryService cycle failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task RecoverStuckMeetingsAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - StaleAfter;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var stuck = await db.Meetings
            .Where(m => (m.Status == MeetingStatus.Pending || m.Status == MeetingStatus.Joining)
                     && m.UpdatedAt < cutoff)
            .Select(m => new { m.Id, m.Status, m.BotTaskArn })
            .ToListAsync(ct);

        if (stuck.Count == 0) return;

        _logger.LogInformation("FIRM: StuckMeetingRecovery — {Count} meeting(s) in Pending/Joining for over {Minutes} minutes", stuck.Count, StaleAfter.TotalMinutes);

        var cluster = _config["Firm:EcsCluster"];
        var taskStatuses = new Dictionary<string, string>();
        var arns = stuck.Where(m => !string.IsNullOrEmpty(m.BotTaskArn)).Select(m => m.BotTaskArn!).Distinct().ToList();

        if (arns.Count > 0)
        {
            if (string.IsNullOrEmpty(cluster))
            {
                _logger.LogWarning("FIRM: StuckMeetingRecovery — Firm:EcsCluster not configured; cannot check bot task status, skipping cycle");
                return;
            }

            // DescribeTasks accepts at most 100 task ARNs per call. ARNs ECS no longer knows
            // about (stopped tasks age out after ~1h) come back in Failures and are simply
            // absent from taskStatuses, which is treated as "task not found" below.
            foreach (var chunk in arns.Chunk(100))
            {
                var response = await _ecs.DescribeTasksAsync(new DescribeTasksRequest
                {
                    Cluster = cluster,
                    Tasks = chunk.ToList()
                }, ct);
                foreach (var task in response.Tasks)
                    taskStatuses[task.TaskArn] = task.LastStatus;
            }
        }

        foreach (var m in stuck)
        {
            string? lastStatus = null;
            if (!string.IsNullOrEmpty(m.BotTaskArn))
                taskStatuses.TryGetValue(m.BotTaskArn, out lastStatus);

            // Anything other than STOPPED (PROVISIONING, PENDING, ACTIVATING, RUNNING, or on its
            // way down via STOPPING/DEPROVISIONING) is left for a later cycle.
            if (lastStatus != null && lastStatus != "STOPPED")
            {
                _logger.LogDebug("FIRM: StuckMeetingRecovery — meeting {Id} bot task {Arn} is {TaskStatus}; skipping", m.Id, m.BotTaskArn, lastStatus);
                continue;
            }

            // Re-read and re-check before writing so a bot callback that landed since the query
            // (status moved on, or a retry launched a new task) is never clobbered.
            var meeting = await db.Meetings.FirstOrDefaultAsync(x => x.Id == m.Id, ct);
            if (meeting == null
                || meeting.Status is not (MeetingStatus.Pending or MeetingStatus.Joining)
                || meeting.BotTaskArn != m.BotTaskArn)
                continue;

            meeting.Status = MeetingStatus.Scheduled;
            meeting.LastFailureReason = FailureReason;
            meeting.BotTaskArn = null;
            meeting.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _logger.LogWarning(
                "FIRM: StuckMeetingRecovery — meeting {Id} reverted {OldStatus} → Scheduled (bot task {Arn}: {TaskStatus}), LastFailureReason={Reason}",
                m.Id, m.Status, m.BotTaskArn ?? "(none)", lastStatus ?? "not found", FailureReason);
        }
    }
}
