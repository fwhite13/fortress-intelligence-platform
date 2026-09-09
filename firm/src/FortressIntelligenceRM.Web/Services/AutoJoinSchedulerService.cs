using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using System.Text.Json;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// Creates and deletes one-shot EventBridge Scheduler schedules that fire the
/// autojoin Lambda at StartDatetime - 2 minutes for scheduled meetings.
/// No-ops when Firm:AutoJoinEnabled is false (default).
/// </summary>
public class AutoJoinSchedulerService
{
    private readonly IAmazonScheduler _scheduler;
    private readonly IConfiguration _config;
    private readonly ILogger<AutoJoinSchedulerService> _logger;
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;

    public AutoJoinSchedulerService(
        IAmazonScheduler scheduler,
        IConfiguration config,
        ILogger<AutoJoinSchedulerService> logger,
        IDbContextFactory<FirmDbContext> dbFactory)
    {
        _scheduler = scheduler;
        _config = config;
        _logger = logger;
        _dbFactory = dbFactory;
    }

    private bool Enabled => _config.GetValue<bool>("Firm:AutoJoinEnabled", false);
    private string ScheduleGroup => _config["Firm:AutoJoinScheduleGroup"] ?? "firm-autojoin";
    private string? LambdaArn => _config["Firm:AutoJoinLambdaArn"];
    private string? SchedulerRoleArn => _config["Firm:AutoJoinSchedulerRoleArn"];

    private string ScheduleName(long meetingId) => $"{ScheduleGroup}-{meetingId}";

    public async Task CreateScheduleAsync(long meetingId, string meetingUrl, DateTime startDatetimeUtc)
    {
        if (!Enabled)
        {
            _logger.LogDebug("FIRM: AutoJoin disabled — skipping schedule creation for meeting {Id}", meetingId);
            return;
        }

        if (string.IsNullOrEmpty(LambdaArn) || string.IsNullOrEmpty(SchedulerRoleArn))
        {
            _logger.LogWarning("FIRM: AutoJoin enabled but Firm:AutoJoinLambdaArn or Firm:AutoJoinSchedulerRoleArn not configured — skipping");
            return;
        }

        var fireAt = startDatetimeUtc.AddMinutes(-2);
        if (fireAt <= DateTime.UtcNow)
        {
            _logger.LogWarning("FIRM: AutoJoin schedule time {FireAt} is in the past for meeting {Id} — skipping", fireAt, meetingId);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            meetingId,
            meetingUrl,
            firmApiUrl = _config["Firm:ApiUrl"] ?? "",
            botCallbackSecret = _config["Firm:BotCallbackSecret"] ?? ""
        });

        try
        {
            await _scheduler.CreateScheduleAsync(new CreateScheduleRequest
            {
                Name = ScheduleName(meetingId),
                GroupName = ScheduleGroup,
                ScheduleExpression = $"at({fireAt:yyyy-MM-ddTHH:mm:ss})",
                ScheduleExpressionTimezone = "UTC",
                FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF },
                Target = new Target
                {
                    Arn = LambdaArn,
                    RoleArn = SchedulerRoleArn,
                    Input = payload
                },
                ActionAfterCompletion = ActionAfterCompletion.DELETE
            });

            _logger.LogInformation("FIRM: AutoJoin schedule created for meeting {Id} firing at {FireAt}", meetingId, fireAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to create AutoJoin schedule for meeting {Id}", meetingId);
            throw;
        }
    }

    /// <summary>
    /// Issue 3 backfill (2026-09-09): the EventBridge schedule payload only started carrying
    /// <c>botCallbackSecret</c> as of commit 5ce95fdc (2026-09-03 20:35 EDT). Any Scheduled,
    /// auto-added meeting created before that timestamp still has a stale schedule missing the
    /// field. The Lambda's BOT_CALLBACK_SECRET env-var fallback covers it in the meantime, but
    /// this recreates each affected schedule with the current payload format so the schedule
    /// itself is authoritative again. Runs once on every app startup — CreateScheduleAsync
    /// upserts (EventBridge Scheduler CreateSchedule overwrites an existing schedule of the same
    /// name), so this is safe to run repeatedly.
    /// </summary>
    public async Task BackfillStaleSchedulesAsync(CancellationToken ct = default)
    {
        if (!Enabled)
        {
            _logger.LogDebug("FIRM: AutoJoin disabled — skipping startup schedule backfill");
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var candidates = await db.Meetings
            .Where(m => m.Status == MeetingStatus.Scheduled
                     && m.StartDatetime != null
                     && m.StartDatetime > DateTime.UtcNow
                     && m.Source == "autoadd")
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            _logger.LogDebug("FIRM: Startup schedule backfill — no eligible meetings found");
            return;
        }

        _logger.LogInformation("FIRM: Startup schedule backfill — refreshing {Count} EventBridge schedule(s)", candidates.Count);

        foreach (var meeting in candidates)
        {
            try
            {
                await CreateScheduleAsync(meeting.Id, meeting.MeetingUrl ?? "", meeting.StartDatetime!.Value);
                _logger.LogInformation("FIRM: Refreshed EventBridge schedule for meeting {Id} (backfill)", meeting.Id);
            }
            catch (Exception ex)
            {
                // Per-meeting failure must not abort the rest of the backfill loop.
                _logger.LogError(ex, "FIRM: Startup schedule backfill failed for meeting {Id}", meeting.Id);
            }
        }
    }

    public async Task DeleteScheduleAsync(long meetingId)
    {
        if (!Enabled) return;

        try
        {
            await _scheduler.DeleteScheduleAsync(new DeleteScheduleRequest
            {
                Name = ScheduleName(meetingId),
                GroupName = ScheduleGroup
            });
            _logger.LogInformation("FIRM: AutoJoin schedule deleted for meeting {Id}", meetingId);
        }
        catch (ResourceNotFoundException)
        {
            // Already gone (fired, manually deleted, or never created) — not an error
            _logger.LogDebug("FIRM: AutoJoin schedule not found for meeting {Id} — nothing to delete", meetingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to delete AutoJoin schedule for meeting {Id}", meetingId);
            // Non-fatal — log and continue
        }
    }
}
