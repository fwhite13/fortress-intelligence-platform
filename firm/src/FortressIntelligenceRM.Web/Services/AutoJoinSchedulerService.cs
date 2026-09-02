using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using System.Text.Json;

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

    public AutoJoinSchedulerService(IAmazonScheduler scheduler, IConfiguration config, ILogger<AutoJoinSchedulerService> logger)
    {
        _scheduler = scheduler;
        _config = config;
        _logger = logger;
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
