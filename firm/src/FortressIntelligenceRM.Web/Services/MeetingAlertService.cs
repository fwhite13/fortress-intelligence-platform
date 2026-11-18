using System.Net;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// WI #9644 — owner-only problem emails.
///
/// Two emails, both to the meeting row's own owner (CreatedBy user's Email) and never to anyone else:
///  * Admission warning: the Notetaker has not been admitted 3 minutes after the scheduled start
///    (sent by <see cref="MeetingAdmissionWatchService"/>).
///  * Failure alert: the row went Failed (<see cref="NotifyFailure"/>, called from every place that
///    sets a meeting to Failed). One email per failed run, deduped by failure_alert_sent_at, which
///    is cleared when the same row starts a new run.
///
/// Master gate: nothing is sent unless Firm:OwnerAlertsEnabled is true (default false; dev shares the
/// database with prod). Per-user gate: FirmUser.NotifyMeetingProblems (Settings toggle, default on).
///
/// The qualification rules are pure static functions over (meeting row, owner, now) so they are easy to
/// reason about and test; the I/O around them only loads rows, claims the row atomically, and sends.
/// Sends never throw into, or delay, the caller: <see cref="NotifyFailure"/> returns immediately and the
/// work runs on the thread pool inside try/catch. A SES error never changes meeting state.
///
/// Logs carry the meeting id, the alert kind and a skip/failure reason only: never an email address,
/// the title or the email body.
/// </summary>
public class MeetingAlertService
{
    public const string EnabledConfigKey = "Firm:OwnerAlertsEnabled";

    /// <summary>Warn once this long after the scheduled start if the bot still has not joined.</summary>
    public static readonly TimeSpan AdmissionWarningAfter = TimeSpan.FromMinutes(3);
    /// <summary>Never warn about a meeting that started longer ago than this.</summary>
    public static readonly TimeSpan AdmissionWarningUntil = TimeSpan.FromMinutes(60);
    /// <summary>No failure alert for rows whose meeting/recording is older than this at the moment of failing
    /// (backfills, bulk watchdog sweeps of old rows).</summary>
    public static readonly TimeSpan FailureAlertMaxAge = TimeSpan.FromHours(24);

    /// <summary>Statuses in which the bot has not yet been admitted (pre-recording).</summary>
    private static readonly MeetingStatus[] PreRecordingStatuses =
        { MeetingStatus.Scheduled, MeetingStatus.Pending, MeetingStatus.Joining };

    /// <summary>Sources that never have a bot (no meeting URL): Plaud imports, mobile and browser uploads.</summary>
    private static readonly string[] NonBotSources = { "plaud", "mobile", FortressIntelligenceRM.Web.Controllers.RecordingUploadService.Source };

    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly BrandingConfig _branding;
    private readonly ILogger<MeetingAlertService> _logger;

    public MeetingAlertService(IDbContextFactory<FirmDbContext> dbFactory, IServiceScopeFactory scopeFactory,
        IConfiguration config, BrandingConfig branding, ILogger<MeetingAlertService> logger)
    {
        _dbFactory = dbFactory;
        _scopeFactory = scopeFactory;
        _config = config;
        _branding = branding;
        _logger = logger;
    }

    /// <summary>Firm:OwnerAlertsEnabled (default false).</summary>
    public bool Enabled => _config.GetValue<bool?>(EnabledConfigKey) ?? false;

    // ───────────────────────────── pure rules ─────────────────────────────

    /// <summary>True for meetings a bot joins: a meeting URL and not a Plaud/mobile/upload source.</summary>
    public static bool IsBotMeeting(FirmMeeting m) =>
        !string.IsNullOrWhiteSpace(m.MeetingUrl)
        && !NonBotSources.Contains(m.Source ?? "", StringComparer.OrdinalIgnoreCase);

    /// <summary>Null when the owner may be emailed, else the skip reason.</summary>
    public static string? GetOwnerSkipReason(FirmUser? owner)
    {
        if (owner == null) return "no-owner";
        if (!owner.IsActive) return "owner-inactive";
        if (string.IsNullOrWhiteSpace(owner.Email)) return "owner-no-email";
        if (!owner.NotifyMeetingProblems) return "owner-opted-out";
        return null;
    }

    /// <summary>Null when the admission warning should be sent for this row now, else the skip reason.</summary>
    public static string? GetAdmissionWarningSkipReason(FirmMeeting m, FirmUser? owner, DateTime nowUtc)
    {
        if (m.AdmissionWarningSentAt != null) return "already-sent";
        if (m.StartDatetime is not DateTime start) return "no-start-time";
        var sinceStart = nowUtc - start;
        if (sinceStart < AdmissionWarningAfter) return "too-early";
        if (sinceStart > AdmissionWarningUntil) return "too-late";
        if (m.StartedAt != null) return "already-joined";
        if (!PreRecordingStatuses.Contains(m.Status)) return "not-pre-recording";
        if (!IsBotMeeting(m)) return "not-bot-meeting";
        return GetOwnerSkipReason(owner);
    }

    /// <summary>When a row counts as "happening": scheduled start, else creation time.</summary>
    public static DateTime ReferenceTime(FirmMeeting m) => m.StartDatetime ?? m.ScheduledAt ?? m.CreatedAt;

    /// <summary>Null when the failure alert should be sent for this row now, else the skip reason.</summary>
    public static string? GetFailureSkipReason(FirmMeeting m, FirmUser? owner, DateTime nowUtc)
    {
        if (m.Status != MeetingStatus.Failed) return "not-failed";
        if (m.FailureAlertSentAt != null) return "already-sent";
        if (nowUtc - ReferenceTime(m) > FailureAlertMaxAge) return "older-than-24h";
        return GetOwnerSkipReason(owner);
    }

    // ───────────────────────────── email text ─────────────────────────────

    public sealed record AlertEmail(string Subject, string Text, string Html);

    /// <summary>Plain-words reason for a failure, from the existing UI/controller messages.</summary>
    /// <summary>The reason code to use for the email: LastFailureReason, or one inferred from the bot's error text
    /// when the bot sent none (Google Meet blocks, the pre-recording lobby check).</summary>
    public static string? ClassifyReason(string? reasonCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(reasonCode)) return reasonCode.Trim().ToLowerInvariant();
        var e = errorMessage ?? "";
        if (e.Contains("Google Meet", StringComparison.OrdinalIgnoreCase) && e.Contains("block", StringComparison.OrdinalIgnoreCase))
            return "google_meet_blocked";
        if (e.Contains("not admitted", StringComparison.OrdinalIgnoreCase)) return "lobby_timeout";
        return null;
    }

    public static string FailureReasonText(string? reasonCode) => reasonCode switch
    {
        "lobby_timeout" => "The Notetaker waited in the meeting lobby, but nobody admitted it before the wait ran out.",
        "join_denied" => "Teams denied the Notetaker access to the meeting. Someone in the meeting declined the request, or the meeting's settings do not allow it to join.",
        "no_primary_joined" => "No recording bot was able to join this meeting.",
        "primary_failed_window_passed" => "The recording bot failed and the meeting's join window has passed.",
        "bot_launch_failed" => "The recording bot stopped before it could join the meeting.",
        "zoom_not_connected" => "Your Zoom account is not connected, so the Notetaker could not join the Zoom meeting. Connect it in Settings, then try again.",
        "zoom_obf_unavailable" => "Zoom authorization was unavailable for this meeting. Make sure your Zoom account is connected in Settings, then try again.",
        "google_meet_blocked" => "Google Meet did not let the Notetaker join this meeting.",
        StaleMeetingWatchdogService.FailureReason => StaleMeetingWatchdogService.UserMessage,
        "transcription_submit_failed" => "The recording was received, but transcription could not be started.",
        IngestFailedReason => "The recording could not be stored, so it was not transcribed.",
        "dead_room" => "The Notetaker joined, but the meeting closed almost immediately with nobody in it.",
        _ => "Something went wrong and the meeting could not be recorded."
    };

    /// <summary>Reason code used for the mobile/upload/Plaud ingest storage failures, which carry no LastFailureReason.</summary>
    public const string IngestFailedReason = "ingest_failed";

    /// <summary>What the owner can do next, by reason.</summary>
    public static string FailureNextStepText(string? reasonCode) => reasonCode switch
    {
        "lobby_timeout" or "join_denied" or "no_primary_joined" or "bot_launch_failed" or "primary_failed_window_passed" =>
            "If the meeting is still running, ask the organizer or a presenter to admit the Notetaker (or let it bypass the lobby) and use Join Meeting on the meeting page to try again.",
        "zoom_not_connected" or "zoom_obf_unavailable" =>
            "Connect your Zoom account in Settings, then use Join Meeting on the meeting page to try again while the meeting is still running.",
        StaleMeetingWatchdogService.FailureReason or "transcription_submit_failed" =>
            "You can retry the transcription from the meeting page.",
        IngestFailedReason => "Please upload the recording again.",
        _ => "Open the meeting page for details. If the meeting is still running you can use Join Meeting to try again."
    };

    public static AlertEmail BuildAdmissionWarningEmail(string title, DateTime startUtc, string botName, int lobbyWaitMinutes,
        string? meetingLink)
    {
        var when = FormatTime(startUtc);
        var subject = $"Your Notetaker hasn't been admitted to {OneLine(title)}";
        var lines = new List<string>
        {
            $"Your meeting \"{title}\" was scheduled to start at {when}.",
            $"{botName} has not been admitted yet: it is waiting to be let in, or it has not been able to join.",
            "Please ask the meeting organizer or a presenter to admit it from the lobby.",
            $"If nobody admits it, it will give up after the {lobbyWaitMinutes}-minute wait and email you again."
        };
        return Compose(subject, lines, meetingLink);
    }

    public static AlertEmail BuildFailureEmail(string title, DateTime? scheduledUtc, string botName, string? reasonCode,
        string? meetingLink)
    {
        var subject = $"The Notetaker couldn't record {OneLine(title)}";
        var lines = new List<string>
        {
            scheduledUtc is DateTime s
                ? $"{botName} couldn't record your meeting \"{title}\", scheduled for {FormatTime(s)}."
                : $"{botName} couldn't record your meeting \"{title}\".",
            $"What happened: {FailureReasonText(reasonCode)}",
            $"What to do next: {FailureNextStepText(reasonCode)}"
        };
        return Compose(subject, lines, meetingLink);
    }

    private static AlertEmail Compose(string subject, List<string> lines, string? link)
    {
        var text = string.Join("\n\n", lines);
        var html = string.Join("", lines.Select(l => $"<p>{WebUtility.HtmlEncode(l)}</p>"));
        if (!string.IsNullOrEmpty(link))
        {
            text += $"\n\nOpen the meeting: {link}";
            var enc = WebUtility.HtmlEncode(link);
            html += $"<p><a href=\"{enc}\">Open the meeting</a></p>";
        }
        html = $"<html><body style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1a2332\">{html}</body></html>";
        return new AlertEmail(subject, text, html);
    }

    private static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static readonly TimeZoneInfo? Eastern = FindEastern();
    private static TimeZoneInfo? FindEastern()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch { return null; }
    }

    /// <summary>Same zone the meeting list uses (America/New_York); UTC if the zone is unavailable.</summary>
    public static string FormatTime(DateTime utc)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (Eastern == null) return $"{utc:ddd MMM d, h:mm tt} UTC";
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, Eastern);
        var abbr = Eastern.IsDaylightSavingTime(local) ? "EDT" : "EST";
        return $"{local:ddd MMM d, h:mm tt} {abbr}";
    }

    // ───────────────────────────── I/O ─────────────────────────────

    private string BotName => _branding.NotetakerName;

    /// <summary>Lobby wait the bot uses (Firm:LobbyTimeoutSeconds, bot default 300 s), in whole minutes.</summary>
    private int LobbyWaitMinutes
    {
        get
        {
            var configured = _config.GetValue<int?>("Firm:LobbyTimeoutSeconds");
            var seconds = configured is > 0 ? configured.Value : 300;
            return Math.Max(1, (int)Math.Round(seconds / 60.0));
        }
    }

    private string? MeetingLink(long meetingId)
    {
        var baseUrl = _config["Firm:ApiUrl"]?.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl}/meetings/{meetingId}";
    }

    /// <summary>
    /// Fire-and-forget: queue the failure alert for one row and return immediately. Safe to call from any
    /// Failed transition; it never throws and never blocks the caller. Cheap no-op when alerts are off.
    /// <paramref name="reasonOverride"/> is used when the row carries no LastFailureReason (ingest failures).
    /// </summary>
    public void NotifyFailure(long meetingId, string? reasonOverride = null)
    {
        try
        {
            if (!Enabled) return;
            _ = Task.Run(() => NotifyFailureAsync(meetingId, reasonOverride));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("FIRM: Owner alert (failure) for meeting {Id} could not be queued: {Error}", meetingId, ex.GetType().Name);
        }
    }

    /// <summary>Fire-and-forget for several rows (a batch of subscribers failed in one save).</summary>
    public void NotifyFailures(IEnumerable<long> meetingIds, string? reasonOverride = null)
    {
        foreach (var id in meetingIds) NotifyFailure(id, reasonOverride);
    }

    /// <summary>
    /// Sends the failure alert for one row if it qualifies. Never throws. Claims the row with
    /// UPDATE ... SET failure_alert_sent_at = UTC_TIMESTAMP() WHERE ... IS NULL before sending, so the
    /// controller's retry, a second instance or a repeated call never double-sends.
    /// </summary>
    public async Task NotifyFailureAsync(long meetingId, string? reasonOverride = null)
    {
        const string kind = "failure";
        try
        {
            if (!Enabled) return;
            var now = DateTime.UtcNow;
            await using var db = await _dbFactory.CreateDbContextAsync();
            var meeting = await db.Meetings.AsNoTracking().Include(m => m.CreatedByUser)
                .FirstOrDefaultAsync(m => m.Id == meetingId);
            if (meeting == null) { LogSkipped(kind, meetingId, "not-found"); return; }

            var skip = GetFailureSkipReason(meeting, meeting.CreatedByUser, now);
            if (skip != null) { LogSkipped(kind, meetingId, skip); return; }
            // Ruling D14: no user notifications for Plaud backfills or recordings more than 24 h old at import.
            if (await FortressIntelligenceRM.Web.Services.Plaud.PlaudSyncService.SuppressUserNotificationsAsync(db, meeting))
            { LogSkipped(kind, meetingId, "plaud-backfill-or-old"); return; }

            var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE firm_meetings SET failure_alert_sent_at = UTC_TIMESTAMP() WHERE id = {meetingId} AND failure_alert_sent_at IS NULL AND status = 'Failed'");
            if (claimed != 1) { LogSkipped(kind, meetingId, "claimed-elsewhere"); return; }

            var reason = ClassifyReason(meeting.LastFailureReason, meeting.ErrorMessage) ?? reasonOverride;
            var email = BuildFailureEmail(meeting.Title ?? $"Meeting {meeting.Id}", meeting.StartDatetime ?? meeting.ScheduledAt,
                BotName, reason, MeetingLink(meeting.Id));
            await SendAsync(kind, meeting.Id, meeting.CreatedByUser!.Email, email, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("FIRM: Owner alert ({Kind}) for meeting {Id} failed: {Error}", kind, meetingId, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Sends the admission warning for one row (with CreatedByUser loaded) if it qualifies. Never throws.
    /// Returns true when an email was sent.
    /// </summary>
    public async Task<bool> SendAdmissionWarningAsync(FirmMeeting meeting, DateTime nowUtc, CancellationToken ct = default)
    {
        const string kind = "admission-warning";
        try
        {
            if (!Enabled) return false;
            var skip = GetAdmissionWarningSkipReason(meeting, meeting.CreatedByUser, nowUtc);
            if (skip != null) { LogSkipped(kind, meeting.Id, skip); return false; }

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE firm_meetings SET admission_warning_sent_at = UTC_TIMESTAMP() WHERE id = {meeting.Id} AND admission_warning_sent_at IS NULL AND started_at IS NULL",
                ct);
            if (claimed != 1) { LogSkipped(kind, meeting.Id, "claimed-elsewhere"); return false; }

            var email = BuildAdmissionWarningEmail(meeting.Title ?? $"Meeting {meeting.Id}", meeting.StartDatetime!.Value,
                BotName, LobbyWaitMinutes, MeetingLink(meeting.Id));
            return await SendAsync(kind, meeting.Id, meeting.CreatedByUser!.Email, email, null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("FIRM: Owner alert ({Kind}) for meeting {Id} failed: {Error}", kind, meeting.Id, ex.GetType().Name);
            return false;
        }
    }

    private async Task<bool> SendAsync(string kind, long meetingId, string toEmail, AlertEmail email, string? reason)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            await emailService.SendNotificationAsync(toEmail, email.Subject, email.Text, email.Html);
            _logger.LogInformation("FIRM: Owner alert ({Kind}) sent for meeting {Id} (reason={Reason})", kind, meetingId, reason ?? "-");
            return true;
        }
        catch (Exception ex)
        {
            // The claim stays: one attempt per run (no retry logic in scope). SES messages can contain the
            // recipient address, so only the exception type is logged.
            _logger.LogWarning("FIRM: Owner alert ({Kind}) send failed for meeting {Id}: {Error}", kind, meetingId, ex.GetType().Name);
            return false;
        }
    }

    private void LogSkipped(string kind, long meetingId, string reason) =>
        _logger.LogInformation("FIRM: Owner alert ({Kind}) skipped for meeting {Id} ({Reason})", kind, meetingId, reason);
}
