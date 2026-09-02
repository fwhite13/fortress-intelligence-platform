using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

public class CalendarAutoSyncService : IHostedService, IDisposable
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly CalendarService _calendarService;
    private readonly MeetingService _meetingService;
    private readonly AutoJoinSchedulerService _autoJoinScheduler;
    private readonly ILogger<CalendarAutoSyncService> _logger;
    private readonly IConfiguration _config;
    private Timer? _timer;

    public CalendarAutoSyncService(
        IDbContextFactory<FirmDbContext> dbFactory,
        CalendarService calendarService,
        MeetingService meetingService,
        AutoJoinSchedulerService autoJoinScheduler,
        ILogger<CalendarAutoSyncService> logger,
        IConfiguration config)
    {
        _dbFactory = dbFactory;
        _calendarService = calendarService;
        _meetingService = meetingService;
        _autoJoinScheduler = autoJoinScheduler;
        _logger = logger;
        _config = config;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var intervalMinutes = _config.GetValue<int>("Firm:CalendarSyncIntervalMinutes", 15);
        _logger.LogInformation("[AutoSync] Service started. Poll interval: {Minutes}m", intervalMinutes);
        _timer = new Timer(PollAsync, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(intervalMinutes));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async void PollAsync(object? state)
    {
        try
        {
            await PollCoreAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoSync] Unhandled error in poll cycle");
        }
    }

    private async Task PollCoreAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var users = await db.Users
            .Where(u => u.AutoAddCalendarMeetings && u.IsActive)
            .ToListAsync(ct);

        if (users.Count == 0) return;

        _logger.LogInformation("[AutoSync] Polling calendars for {Count} opted-in users.", users.Count);

        foreach (var user in users)
        {
            try
            {
                var meetings = await _calendarService.GetUpcomingCalendarMeetingsAsync(user.EntraOid, user.Email, ct);

                foreach (var dto in meetings)
                {
                    var exists = await db.Meetings.AnyAsync(
                        m => m.CalendarEventId == dto.CalendarEventId && m.CreatedBy == user.Id, ct);
                    if (exists) continue;

                    var startDatetime = DateTime.Parse(dto.StartDateTime);

                    // EF Core sentinel workaround: MeetingStatus.Scheduled == 0 (CLR default), so
                    // EF treats it as unset and omits from INSERT, letting DB default (Joining) win.
                    // Insert as Joining, then UPDATE to Scheduled to bypass the sentinel check.
                    var meeting = new FirmMeeting
                    {
                        Status = MeetingStatus.Joining,
                        Platform = dto.Platform,
                        MeetingUrl = dto.JoinUrl,
                        CalendarEventId = dto.CalendarEventId,
                        Title = dto.Subject,
                        StartDatetime = startDatetime,
                        CreatedBy = user.Id,
                        CreatorEntraOid = user.EntraOid,
                        Source = "autoadd",
                        Mode = dto.Mode,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    db.Meetings.Add(meeting);
                    await db.SaveChangesAsync(ct);
                    await _meetingService.UpdateStatusAsync(meeting.Id, MeetingStatus.Scheduled);

                    await _autoJoinScheduler.CreateScheduleAsync(meeting.Id, dto.JoinUrl, startDatetime);

                    _logger.LogInformation("[AutoSync] Added meeting {Id} from calendar {CalendarEventId} for user {UserId}",
                        meeting.Id, dto.CalendarEventId, user.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AutoSync] Failed to sync calendar for user {UserId}", user.Id);
            }
        }
    }
}
