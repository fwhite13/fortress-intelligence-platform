using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

public class CalendarAutoSyncService : IHostedService, IDisposable
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly CalendarService _calendarService;
    private readonly AutoJoinSchedulerService _autoJoinScheduler;
    private readonly MeetingService _meetingService;
    private readonly ILogger<CalendarAutoSyncService> _logger;
    private readonly IConfiguration _config;
    private Timer? _timer;

    public CalendarAutoSyncService(
        IDbContextFactory<FirmDbContext> dbFactory,
        CalendarService calendarService,
        AutoJoinSchedulerService autoJoinScheduler,
        MeetingService meetingService,
        ILogger<CalendarAutoSyncService> logger,
        IConfiguration config)
    {
        _dbFactory = dbFactory;
        _calendarService = calendarService;
        _autoJoinScheduler = autoJoinScheduler;
        _meetingService = meetingService;
        _logger = logger;
        _config = config;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var intervalMinutes = _config.GetValue<int>("Firm:CalendarSyncIntervalMinutes", 15);
        _logger.LogInformation("[AutoSync] Service started. Poll interval: {Minutes}m", intervalMinutes);
        _timer = new Timer(PollAsync, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(intervalMinutes));

        // Issue 3: refresh any stale pre-5ce95fdc EventBridge schedules on every startup.
        // Fire-and-forget — a large backlog must not delay host startup; failures are
        // caught/logged per-meeting inside BackfillStaleSchedulesAsync.
        _ = RunStartupScheduleBackfillAsync();

        return Task.CompletedTask;
    }

    private async Task RunStartupScheduleBackfillAsync()
    {
        try
        {
            await _autoJoinScheduler.BackfillStaleSchedulesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoSync] Unhandled error during startup AutoJoin schedule backfill");
        }
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
                    // Primary dedup: exact calendar event ID match
                    var exists = await db.Meetings.AnyAsync(
                        m => m.CalendarEventId == dto.CalendarEventId && m.CreatedBy == user.Id, ct);
                    if (exists) continue;

                    var startDatetime = DateTime.Parse(dto.StartDateTime);

                    // Secondary dedup: same user + URL + start time (guards against MS Graph ID instability
                    // on recurring occurrences returning different IDs across poll cycles)
                    // Note: no index on (created_by, meeting_url, start_datetime) — could be added if user base grows
                    var duplicate = await db.Meetings.FirstOrDefaultAsync(
                        m => m.MeetingUrl == dto.JoinUrl
                          && m.StartDatetime == startDatetime
                          && m.CreatedBy == user.Id, ct);
                    if (duplicate != null)
                    {
                        // Upsert the CalendarEventId to the latest value Graph returned
                        if (duplicate.CalendarEventId != dto.CalendarEventId)
                        {
                            duplicate.CalendarEventId = dto.CalendarEventId;
                            duplicate.UpdatedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync(ct);
                            _logger.LogInformation(
                                "[AutoSync] Updated CalendarEventId on meeting {Id} (Graph ID drift on recurring occurrence)",
                                duplicate.Id);
                        }
                        continue;
                    }

                    var meeting = new FirmMeeting
                    {
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

                    await InsertScheduledMeetingAsync(db, meeting, ct);

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

    /// <summary>
    /// Inserts a new calendar-sourced meeting and lands it in MeetingStatus.Scheduled — the single,
    /// shared copy of the EF-sentinel workaround (ADO#17 diagnostic, 2026-09-09; previously duplicated
    /// in the now-deleted MeetingService.UpsertFromCalendarAsync, which had zero callers).
    ///
    /// NOTE: MeetingStatus.Scheduled == 0, which is the CLR default for the enum. EF Core's
    /// HasDefaultValue(MeetingStatus.Joining) treats Status as ValueGenerated.OnAdd, so on INSERT
    /// it compares against the CLR sentinel (0/Scheduled) and — seeing a match — omits the column
    /// entirely, letting the DB default (Joining) win. Inserting as Joining avoids the sentinel
    /// match; the follow-up UpdateStatusAsync flips it to Scheduled via UPDATE, which is not subject
    /// to the same sentinel check.
    ///
    /// FirmDbContext now also configures .HasSentinel(MeetingStatus.Joining), which should make this
    /// two-step dance unnecessary going forward — kept in place as defense-in-depth until that's
    /// verified in production.
    /// </summary>
    private async Task<FirmMeeting> InsertScheduledMeetingAsync(FirmDbContext db, FirmMeeting draft, CancellationToken ct)
    {
        draft.Status = MeetingStatus.Joining;
        db.Meetings.Add(draft);
        await db.SaveChangesAsync(ct);

        await _meetingService.UpdateStatusAsync(draft.Id, MeetingStatus.Scheduled);
        draft.Status = MeetingStatus.Scheduled;

        return draft;
    }
}
