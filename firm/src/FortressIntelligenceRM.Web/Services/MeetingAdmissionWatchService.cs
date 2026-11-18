using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// WI #9644 — emails a meeting's owner (only) once when the Notetaker has not been admitted 3 minutes
/// after the scheduled start.
///
/// Every 30 s it selects bot meetings (a meeting URL, not Plaud/mobile/upload) whose StartDatetime is
/// 3 to 60 minutes ago, that have not started (StartedAt null), are still Scheduled/Pending/Joining and
/// have no admission_warning_sent_at. Each candidate is re-checked by
/// <see cref="MeetingAlertService.GetAdmissionWarningSkipReason"/> (owner active, has an email, toggle on)
/// and claimed with a guarded UPDATE before sending, so two instances never double-send.
///
/// Runs only where AutoJoin scheduling is available (same guard as CalendarAutoSyncService), because
/// dev and prod web share one database; and only when Firm:OwnerAlertsEnabled is true.
/// </summary>
public class MeetingAdmissionWatchService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int MaxPerTick = 50;

    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MeetingAlertService _alerts;
    private readonly ILogger<MeetingAdmissionWatchService> _logger;

    public MeetingAdmissionWatchService(IDbContextFactory<FirmDbContext> dbFactory, IServiceScopeFactory scopeFactory,
        MeetingAlertService alerts, ILogger<MeetingAdmissionWatchService> logger)
    {
        _dbFactory = dbFactory;
        _scopeFactory = scopeFactory;
        _alerts = alerts;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FIRM: Owner alerts are {State} ({Key})",
            _alerts.Enabled ? "ON" : "OFF", MeetingAlertService.EnabledConfigKey);
        if (!_alerts.Enabled) return;

        bool schedulingAvailable;
        using (var scope = _scopeFactory.CreateScope())
            schedulingAvailable = scope.ServiceProvider.GetRequiredService<AutoJoinSchedulerService>().IsSchedulingAvailable;
        if (!schedulingAvailable)
        {
            _logger.LogWarning("FIRM: MeetingAdmissionWatchService NOT started: AutoJoin scheduling is unavailable on this instance (dev and prod share one database; only the scheduling instance sends admission warnings).");
            return;
        }

        _logger.LogInformation("FIRM: MeetingAdmissionWatchService started. Interval: {Interval}", Interval);
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError("FIRM: MeetingAdmissionWatchService cycle failed: {Error}", ex.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var latestStart = now - MeetingAlertService.AdmissionWarningAfter;
        var earliestStart = now - MeetingAlertService.AdmissionWarningUntil;

        List<FirmMeeting> candidates;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            candidates = await db.Meetings
                .AsNoTracking()
                .Include(m => m.CreatedByUser)
                .Where(m => m.AdmissionWarningSentAt == null
                         && m.StartedAt == null
                         && m.StartDatetime != null
                         && m.StartDatetime <= latestStart
                         && m.StartDatetime >= earliestStart
                         && (m.Status == MeetingStatus.Scheduled || m.Status == MeetingStatus.Pending || m.Status == MeetingStatus.Joining)
                         && m.MeetingUrl != null && m.MeetingUrl != ""
                         && m.Source != "plaud" && m.Source != "mobile" && m.Source != "upload"
                         // Owner filter here too, so an opted-out owner is not re-evaluated (and logged) every 30 s.
                         && m.CreatedByUser != null && m.CreatedByUser.IsActive && m.CreatedByUser.NotifyMeetingProblems
                         && m.CreatedByUser.Email != "")
                .OrderBy(m => m.StartDatetime)
                .Take(MaxPerTick)
                .ToListAsync(ct);
        }

        foreach (var meeting in candidates)
        {
            ct.ThrowIfCancellationRequested();
            await _alerts.SendAdmissionWarningAsync(meeting, now, ct);
        }
    }
}
