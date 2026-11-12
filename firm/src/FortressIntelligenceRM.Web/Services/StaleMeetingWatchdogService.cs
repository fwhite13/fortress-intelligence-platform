using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// WI #9132 (spec §5.6, Q8, ruling D19): fails meetings stuck in Transcribing or Summarizing.
/// A Batch job that never posts a callback (spot interruption, attempt timeout, OOM) otherwise
/// leaves the meeting in that state forever: the row cannot be removed from the UI and, for Plaud
/// imports, would hold a Batch-cap slot. Applies to every source (bot, calendar, mobile, Plaud)
/// and every user.
///
/// Every 5 minutes (first run 2 minutes after startup) any meeting whose Status is Transcribing or
/// Summarizing and whose UpdatedAt is older than Firm:StaleTranscribingHours (default 4) is moved
/// to Failed with LastFailureReason = "transcription_timeout" through MeetingService.UpdateStatusAsync,
/// so subscriber promotion and the error-field rules stay in one place. The transition is a
/// conditional UPDATE (still in Transcribing/Summarizing and still stale), so when several
/// firm-web tasks run the watchdog at once each row is failed exactly once.
///
/// Recording, Joining and every other status are never selected: a live bot meeting can
/// legitimately run for hours. A meeting failed this way can be retried with the existing
/// retranscribe endpoint and removed with the normal delete.
///
/// Logs carry meeting id, source and age only (never title or transcript text).
/// </summary>
public class StaleMeetingWatchdogService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    public const double DefaultStaleTranscribingHours = 4;
    // Guard against a mis-set value (0, negative) failing every in-flight transcription.
    private const double MinStaleTranscribingHours = 1;
    private const int MaxPerTick = 200;
    public const string FailureReason = "transcription_timeout";
    public const string UserMessage = "Transcription did not finish. You can retry it from the meeting page.";

    // The only statuses this watchdog ever selects or transitions from.
    private static readonly MeetingStatus[] StuckStatuses = { MeetingStatus.Transcribing, MeetingStatus.Summarizing };

    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<StaleMeetingWatchdogService> _logger;

    public StaleMeetingWatchdogService(
        IDbContextFactory<FirmDbContext> dbFactory,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<StaleMeetingWatchdogService> logger)
    {
        _dbFactory = dbFactory;
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>Firm:StaleTranscribingHours, default 4, never below 1.</summary>
    private TimeSpan StaleAfter
    {
        get
        {
            var hours = _config.GetValue<double?>("Firm:StaleTranscribingHours") ?? DefaultStaleTranscribingHours;
            if (double.IsNaN(hours) || hours < MinStaleTranscribingHours) hours = MinStaleTranscribingHours;
            return TimeSpan.FromHours(hours);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FIRM: StaleMeetingWatchdogService started. Interval: {Interval}, stale after: {StaleHours}h, first run in {Delay}",
            Interval, StaleAfter.TotalHours, StartupDelay);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    await FailStaleMeetingsAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "FIRM: StaleMeetingWatchdog cycle failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task FailStaleMeetingsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - StaleAfter;

        List<StaleRow> stale;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            stale = await db.Meetings
                .AsNoTracking()
                .Where(m => (m.Status == MeetingStatus.Transcribing || m.Status == MeetingStatus.Summarizing)
                         && m.UpdatedAt < cutoff)
                .OrderBy(m => m.UpdatedAt)
                .Take(MaxPerTick)
                .Select(m => new StaleRow(m.Id, m.Source, m.Status, m.UpdatedAt))
                .ToListAsync(ct);
        }

        if (stale.Count == 0) return;

        _logger.LogWarning("FIRM: StaleMeetingWatchdog — {Count} meeting(s) in Transcribing/Summarizing for over {Hours}h",
            stale.Count, StaleAfter.TotalHours);

        using var scope = _scopeFactory.CreateScope();
        var meetingService = scope.ServiceProvider.GetRequiredService<MeetingService>();

        foreach (var row in stale)
        {
            ct.ThrowIfCancellationRequested();
            var ageHours = Math.Round((now - row.UpdatedAt).TotalHours, 1);
            try
            {
                // Conditional: only if still Transcribing/Summarizing and still older than the cutoff.
                // A callback that landed since the query, a retranscribe that restarted the job, or
                // another firm-web instance that already failed the row all make this a no-op.
                var failed = await meetingService.UpdateStatusAsync(
                    row.Id,
                    MeetingStatus.Failed,
                    UserMessage,
                    lastFailureReason: FailureReason,
                    onlyIfStatusIn: StuckStatuses,
                    onlyIfUpdatedBefore: cutoff);

                if (failed)
                    _logger.LogWarning("FIRM: StaleMeetingWatchdog — meeting {Id} (source={Source}) {OldStatus} → Failed after {AgeHours}h, LastFailureReason={Reason}",
                        row.Id, row.Source, row.Status, ageHours, FailureReason);
                else
                    _logger.LogInformation("FIRM: StaleMeetingWatchdog — meeting {Id} (source={Source}) no longer stale or already handled; skipped",
                        row.Id, row.Source);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "FIRM: StaleMeetingWatchdog — failed to fail meeting {Id} (source={Source}, age {AgeHours}h)",
                    row.Id, row.Source, ageHours);
            }
        }
    }

    private sealed record StaleRow(long Id, string Source, MeetingStatus Status, DateTime UpdatedAt);
}
