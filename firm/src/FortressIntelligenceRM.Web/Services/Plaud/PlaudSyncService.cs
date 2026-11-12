using System.Net;
using System.Text.Json.Nodes;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FortressIntelligenceRM.Web.Services.Plaud;

/// <summary>Outcome of one sync run, for the Settings snackbar.</summary>
public record PlaudSyncRunResult(bool AlreadyRunning, int Found, int Imported, int Waiting, string? ErrorCode)
{
    public static readonly PlaudSyncRunResult Busy = new(true, 0, 0, 0, null);

    /// <summary>WI #9135 "Import older": new ledger rows queued by this click (at most 25).</summary>
    public int Queued { get; init; }
    /// <summary>WI #9135 "Import older": unseen recordings listed in the window but not queued by this click.</summary>
    public int MoreAvailable { get; init; }
    /// <summary>WI #9135 "Import older": listing stopped at its page cap, so more may exist beyond MoreAvailable.</summary>
    public bool MaybeMore { get; init; }
}

/// <summary>Settings status lines (spec §6.1). WI #9135 adds the auto-sync fields.</summary>
public record PlaudSyncStatus(int Imported, int Waiting, int Skipped, DateTime? LastCheckedAt, string State,
    string? LastErrorCode, int BackfillDays, bool WindowLocked)
{
    public bool AutoSync { get; init; }
    public int IntervalMinutes { get; init; } = PlaudSyncService.DefaultIntervalMinutes;
    public DateTime? NextSyncAt { get; init; }
    public DateTime? LastImportAt { get; init; }
    public int LastImportedCount { get; init; }
    public int TooLong { get; init; }
    public int NoAudio { get; init; }
    public DateTime? SyncFrom { get; init; }
}

/// <summary>
/// WI #9135 — per-instance Plaud pacing (spec §5.4), a singleton shared by every Plaud MCP call in this process:
///   - token bucket of <see cref="RequestsPerMinute"/> (30) requests per minute; callers wait for a token;
///   - instance circuit: <see cref="CircuitThreshold"/> (3) × 429 within <see cref="CircuitWindow"/> (5 min)
///     pauses all Plaud calls on this instance for <see cref="CircuitPause"/> (15 min). While paused,
///     <see cref="AcquireAsync"/> throws a 429 <see cref="PlaudMcpException"/> with InstancePaused set and no
///     request is sent.
/// </summary>
public sealed class PlaudRateLimiter
{
    public const int RequestsPerMinute = 30;
    public const int CircuitThreshold = 3;
    public static readonly TimeSpan CircuitWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CircuitPause = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private readonly Queue<DateTime> _recent429 = new();
    private readonly ILogger<PlaudRateLimiter> _logger;
    private double _tokens = RequestsPerMinute;
    private DateTime _lastRefill = DateTime.UtcNow;
    private DateTime _pausedUntil = DateTime.MinValue;

    public PlaudRateLimiter(ILogger<PlaudRateLimiter> logger) => _logger = logger;

    /// <summary>Time left on the instance pause, or null when not paused.</summary>
    public TimeSpan? PausedFor
    {
        get
        {
            lock (_gate)
            {
                var left = _pausedUntil - DateTime.UtcNow;
                return left > TimeSpan.Zero ? left : null;
            }
        }
    }

    /// <summary>Takes one request token, waiting for the bucket to refill. Throws while the instance is paused.</summary>
    public async Task AcquireAsync(CancellationToken ct)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if (_pausedUntil > now)
                    throw new PlaudMcpException("Plaud calls paused on this instance", System.Net.HttpStatusCode.TooManyRequests)
                    {
                        RetryAfter = _pausedUntil - now,
                        InstancePaused = true,
                    };
                var elapsed = (now - _lastRefill).TotalSeconds;
                if (elapsed > 0)
                {
                    _tokens = Math.Min(RequestsPerMinute, _tokens + elapsed * RequestsPerMinute / 60.0);
                    _lastRefill = now;
                }
                if (_tokens >= 1)
                {
                    _tokens -= 1;
                    return;
                }
                wait = TimeSpan.FromSeconds((1 - _tokens) * 60.0 / RequestsPerMinute);
            }
            await Task.Delay(wait < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : wait, ct);
        }
    }

    /// <summary>Records an HTTP 429 from Plaud; the third within 5 minutes pauses the instance for 15 minutes.</summary>
    public void Record429()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            _recent429.Enqueue(now);
            while (_recent429.Count > 0 && now - _recent429.Peek() > CircuitWindow)
                _recent429.Dequeue();
            if (_recent429.Count >= CircuitThreshold)
            {
                _pausedUntil = now + CircuitPause;
                _recent429.Clear();
                _logger.LogWarning("[PlaudSync] instance paused for {Minutes} min after {Count} x 429 within {Window} min",
                    (int)CircuitPause.TotalMinutes, CircuitThreshold, (int)CircuitWindow.TotalMinutes);
            }
        }
    }
}

/// <summary>
/// WI #9134 — Plaud import (spec §4.4–§4.7, §5.2 step list, §8.2 caps).
///
/// <see cref="SyncUserAsync"/> (the caller holds the per-user claim; <see cref="SyncNowAsync"/> takes it):
///   1. housekeeping: stale importing → queued, expired pending_audio → no_audio, orphaned rows → deleted_by_user;
///      retries due submit_failed rows (Batch submit only);
///   2. lists the window (sync_from = now − backfill_days on the first run; later max(sync_from, last_success − 2 d)),
///      up to MaxListPages pages of 20; unseen file ids become queued (or too_long, from the listed duration);
///   3. imports queued / pending_audio / due failed rows oldest-first. The per-run limit and both in-flight caps
///      are checked before each get_file; then get_file → presigned URL (a local only) → PlaudAudio stream to a
///      temp file (byte cap) → MeetingIngestService.IngestRecordingAsync (source = platform = plaud,
///      external_id = plaud:&lt;file_id&gt;) → imported or submit_failed. The temp file is deleted in finally.
///   4. archives Plaud's transcript and note next to the audio (D17), best-effort, after the row is imported.
///
/// A file id that has a ledger row is never imported again unless the row is in
/// <see cref="PlaudImportStatus.Retryable"/>. Logs carry user id, file id, meeting id, bytes, duration,
/// status and codes only: never a title, a presigned URL, a token or a Plaud body.
/// </summary>
public class PlaudSyncService
{
    public const string Source = "plaud";
    public const string AudioHttpClientName = "PlaudAudio";
    private const int PageSize = 20;
    private const int MaxSubmitAttempts = 3;
    private const int MaxFailedAttempts = 3;
    private const int MaxTranscriptPages = 20;
    private static readonly TimeSpan ImportingReclaimAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PendingAudioGiveUpAfter = TimeSpan.FromHours(48);
    private static readonly TimeSpan UserClaimStaleAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SyncNowClaimWait = TimeSpan.FromSeconds(10);
    private static readonly string InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}";

    // WI #9135 — auto-sync schedule (spec §5.3, §5.4).
    public const int DefaultIntervalMinutes = 30;
    public const int MinIntervalMinutes = 10;
    public const int MaxIntervalMinutes = 1440;
    /// <summary>"Check every" choices in Settings (spec §5.3).</summary>
    public static readonly int[] IntervalChoices = { 10, 15, 30, 60, 240 };
    /// <summary>Users claimed per background tick per instance (spec §5.2).</summary>
    public const int DueUsersPerTick = 5;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(24);
    private static readonly TimeSpan BacklogRecheck = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FirstRunSpread = TimeSpan.FromMinutes(2);
    // WI #9135 — "Import older…" (spec §5.3): 25 listed per click.
    public const int ImportOlderPerClick = 25;
    public const int ImportOlderMaxDaysBack = 365;
    private const int ImportOlderMaxPages = 10;

    public const string StateIdle = "idle";
    public const string StateOk = "ok";
    public const string StateBackoff = "backoff";
    public const string StateRateLimited = "rate_limited";
    public const string StateReconnectNeeded = "reconnect_needed";
    public const string StateDisconnected = "disconnected";
    /// <summary>firm_plaud_sync.state values that stop polling (spec §5.2, §5.4).</summary>
    public static readonly string[] NotPolledStates = { StateReconnectNeeded, StateDisconnected };

    /// <summary>Interval in minutes clamped to [10, 1440] (spec §5.3); anything below 10 becomes 10.</summary>
    public static int ClampInterval(int minutes) => Math.Clamp(minutes, MinIntervalMinutes, MaxIntervalMinutes);

    /// <summary>A fresh per-run claim token (nonce first, so truncation to claimed_by's 64 chars never drops it).</summary>
    public static string NewClaimToken()
    {
        var token = $"{Guid.NewGuid():N}:{InstanceId}";
        return token.Length > 64 ? token[..64] : token;
    }

    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IPlaudOAuthService _oauth;
    private readonly PlaudMcpClient _mcp;
    private readonly MeetingIngestService _ingest;
    private readonly MeetingService _meetingService;
    private readonly S3Service _s3;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PlaudOptions _options;
    private readonly ILogger<PlaudSyncService> _logger;

    public PlaudSyncService(IDbContextFactory<FirmDbContext> dbFactory, IPlaudOAuthService oauth, PlaudMcpClient mcp,
        MeetingIngestService ingest, MeetingService meetingService, S3Service s3, IHttpClientFactory httpClientFactory,
        IOptions<PlaudOptions> options, ILogger<PlaudSyncService> logger)
    {
        _dbFactory = dbFactory;
        _oauth = oauth;
        _mcp = mcp;
        _ingest = ingest;
        _meetingService = meetingService;
        _s3 = s3;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        // oauth_env = this instance's redirect host (same derivation as PlaudOAuthService, spec §9.2).
        _envHost = Uri.TryCreate(_options.RedirectUri, UriKind.Absolute, out var redirect) ? redirect.Host : "";
    }

    private readonly string _envHost;

    /// <summary>This instance's Plaud redirect host; only accounts whose oauth_env equals it are polled.</summary>
    public string EnvHost => _envHost;

    // ── Claim (spec §5.2) ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Settings "Sync now": takes the per-user claim (waiting up to 10 s), runs <see cref="SyncUserAsync"/>
    /// and releases the claim in finally. Returns <see cref="PlaudSyncRunResult.Busy"/> if another run holds it.
    /// </summary>
    public async Task<PlaudSyncRunResult> SyncNowAsync(Guid userId, string trigger = PlaudSyncTrigger.Manual,
        CancellationToken ct = default)
    {
        await EnsureSyncRowAsync(userId, ct);
        var claimToken = NewClaimToken();
        if (!await WaitForClaimAsync(userId, claimToken, ct))
        {
            _logger.LogInformation("[PlaudSync] user={UserId} sync now skipped: already running", userId);
            return PlaudSyncRunResult.Busy;
        }
        try
        {
            return await SyncUserAsync(userId, trigger, ct);
        }
        finally
        {
            await ReleaseUserClaimAsync(userId, claimToken, CancellationToken.None);
        }
    }

    /// <summary>Manual callers (Sync now, Import older) wait up to 10 s for the claim (spec §5.2).</summary>
    private async Task<bool> WaitForClaimAsync(Guid userId, string claimToken, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + SyncNowClaimWait;
        while (!await TryClaimUserAsync(userId, claimToken, requireDue: false, ct))
        {
            if (DateTime.UtcNow >= deadline)
                return false;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        return true;
    }

    /// <summary>
    /// The single conditional UPDATE from spec §5.2: ownership is decided by the affected-row count
    /// (1 = this caller owns the user's run; 0 = another run holds a claim younger than 10 minutes).
    /// With <paramref name="requireDue"/> (the background poller) the same UPDATE also re-checks that the
    /// user is still due, so a run another instance just finished (and rescheduled) is not repeated.
    /// </summary>
    public async Task<bool> TryClaimUserAsync(Guid userId, string claimToken, bool requireDue, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var staleBefore = now - UserClaimStaleAfter;
        var target = db.PlaudSyncs
            .Where(s => s.UserId == userId && (s.ClaimedAt == null || s.ClaimedAt < staleBefore));
        if (requireDue)
            target = target.Where(s => s.AutoSync && s.NextSyncAt != null && s.NextSyncAt <= now
                                       && !NotPolledStates.Contains(s.State));
        var rows = await target.ExecuteUpdateAsync(u => u
            .SetProperty(s => s.ClaimedBy, claimToken)
            .SetProperty(s => s.ClaimedAt, now)
            .SetProperty(s => s.UpdatedAt, now), ct);
        return rows == 1;
    }

    /// <summary>Releases this caller's claim. Callers pass CancellationToken.None from their finally block.</summary>
    public async Task ReleaseUserClaimAsync(Guid userId, string claimToken, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            await db.PlaudSyncs
                .Where(s => s.UserId == userId && s.ClaimedBy == claimToken)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.ClaimedBy, (string?)null)
                    .SetProperty(s => s.ClaimedAt, (DateTime?)null)
                    .SetProperty(s => s.UpdatedAt, DateTime.UtcNow), ct);
        }
        catch (Exception ex)
        {
            // The 10-minute stale window frees it anyway.
            _logger.LogWarning("[PlaudSync] user={UserId} claim release failed: {Error}", userId, ex.GetType().Name);
        }
    }

    // ── One run ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// WI #9135 — users the background poller should run now (spec §5.2): auto_sync = 1, state not in
    /// (reconnect_needed, disconnected), next_sync_at &lt;= now, joined to an account row whose oauth_env is this
    /// instance's redirect host (dev and prod share the DB, spec §9.2) and that does not need a reconnect.
    /// Unclaimed (or stale-claimed) users only, soonest first, at most <see cref="DueUsersPerTick"/> (LIMIT 5).
    /// </summary>
    public async Task<List<Guid>> GetDueUserIdsAsync(int limit = DueUsersPerTick, CancellationToken ct = default)
    {
        var envHost = _envHost;
        if (envHost.Length == 0)
            return new List<Guid>();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var staleBefore = now - UserClaimStaleAfter;
        return await (
                from s in db.PlaudSyncs
                join a in db.PlaudAccounts on s.UserId equals a.UserId
                where s.AutoSync
                      && !NotPolledStates.Contains(s.State)
                      && s.NextSyncAt != null && s.NextSyncAt <= now
                      && a.OAuthEnv == envHost
                      && !a.NeedsReconnect
                      && (s.ClaimedAt == null || s.ClaimedAt < staleBefore)
                orderby s.NextSyncAt
                select s.UserId)
            .Take(Math.Clamp(limit, 1, DueUsersPerTick))
            .ToListAsync(ct);
    }

    /// <summary>One sync run for a user. The caller must hold the user's claim.</summary>
    public Task<PlaudSyncRunResult> SyncUserAsync(Guid userId, string trigger, CancellationToken ct = default)
        => RunAsync(userId, trigger, olderWindow: null, ct);

    /// <summary>
    /// One run. With <paramref name="olderWindow"/> ("Import older…") step 2 lists that one-off window
    /// instead of the regular one and queues at most 25 new rows as backfill.
    /// </summary>
    private async Task<PlaudSyncRunResult> RunAsync(Guid userId, string trigger, (DateTime From, DateTime To)? olderWindow,
        CancellationToken ct)
    {
        trigger = trigger is PlaudSyncTrigger.Auto or PlaudSyncTrigger.Backfill ? trigger : PlaudSyncTrigger.Manual;
        var sync = await EnsureSyncRowAsync(userId, ct);
        var user = await _meetingService.GetUserAsync(userId);
        if (user == null)
            return new PlaudSyncRunResult(false, 0, 0, 0, "no_user");

        var token = await _oauth.GetAccessTokenAsync(userId, false, ct);
        if (token == null)
        {
            var status = await _oauth.GetStatusAsync(userId, ct);
            var (state, code) = !status.Connected ? (StateDisconnected, "not_connected")
                : status.NeedsReconnect ? (StateReconnectNeeded, "reconnect_needed")
                : status.OtherEnvironment ? (StateIdle, "other_environment")
                : (StateBackoff, "token_unavailable");
            await WriteRunStatusAsync(userId, state, code, success: false, importedCount: 0, retryAfter: null, backlog: false, ct);
            return new PlaudSyncRunResult(false, 0, 0, await CountWaitingAsync(userId, ct), code);
        }

        int found = 0, imported = 0, queued = 0, more = 0;
        var maybeMore = false;
        string? errorCode = null;
        TimeSpan? retryAfter = null;
        try
        {
            var session = await OpenSessionAsync(userId, token, ct);

            // 1. Housekeeping and submit retries.
            await HousekeepAsync(userId, ct);
            imported += await RetrySubmitFailedAsync(userId, ct);

            // 2. List the window (regular, or the one-off "Import older" window).
            if (olderWindow is { } w)
            {
                var older = await ListOlderWindowAsync(session, userId, w.From, w.To, ct);
                (found, queued, more, maybeMore) = (older.Found, older.Queued, older.More, older.MaybeMore);
            }
            else
            {
                found = await ListWindowAsync(session, userId, sync, trigger, ct);
            }

            // 3. Import within the caps.
            imported += await ImportQueuedAsync(session, userId, user.EntraOid, trigger, ct);
        }
        catch (PlaudReconnectNeededException)
        {
            errorCode = "reconnect_needed";
        }
        catch (PlaudMcpException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Spec §5.4: honour Retry-After for this user; the instance circuit is counted by PlaudRateLimiter.
            errorCode = "rate_limited";
            retryAfter = ex.RetryAfter;
            _logger.LogWarning("[PlaudSync] 429 user={UserId} retry_after={RetryAfter} instance_paused={Paused}",
                userId, ex.RetryAfter.HasValue ? ((int)ex.RetryAfter.Value.TotalSeconds).ToString() : "-", ex.InstancePaused);
        }
        catch (PlaudMcpException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            errorCode = "unauthorized";
        }
        catch (PlaudMcpException ex)
        {
            errorCode = ex.StatusCode.HasValue ? $"plaud_{(int)ex.StatusCode.Value}" : "plaud_error";
        }
        catch (HttpRequestException)
        {
            errorCode = "plaud_unreachable";
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            errorCode = "plaud_timeout";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything else still backs off, so a poisoned user is not retried every tick.
            errorCode = "sync_error";
            _logger.LogWarning("[PlaudSync] user={UserId} run failed: {Error}", userId, ex.GetType().Name);
        }

        // A 401 whose forced refresh hit invalid_grant: PlaudOAuthService has flipped needs_reconnect.
        if (errorCode == "unauthorized" && (await _oauth.GetStatusAsync(userId, CancellationToken.None)).NeedsReconnect)
            errorCode = "reconnect_needed";

        var finalState = errorCode switch
        {
            null => StateOk,
            "rate_limited" => StateRateLimited,
            "reconnect_needed" => StateReconnectNeeded,
            _ => StateBackoff,
        };
        var backlog = errorCode == null && await CountQueuedAsync(userId, CancellationToken.None) > 0;
        await WriteRunStatusAsync(userId, finalState, errorCode, success: errorCode == null, imported, retryAfter, backlog,
            CancellationToken.None);

        // "Import older" finished its window: sync_from moves back to the picked date (spec §5.3). While more
        // recordings remain, sync_from stays put so the next click (same date) continues past the ledger.
        if (olderWindow is { } done && errorCode == null && more == 0 && !maybeMore)
            await MoveSyncFromBackAsync(userId, done.From, CancellationToken.None);

        var waiting = await CountWaitingAsync(userId, CancellationToken.None);
        _logger.LogInformation("[PlaudSync] user={UserId} trigger={Trigger} found={Found} queued={Queued} imported={Imported} waiting={Waiting} backlog={Backlog} code={Code}",
            userId, trigger, found, olderWindow.HasValue ? queued : found, imported, waiting, backlog, errorCode ?? "-");
        return new PlaudSyncRunResult(false, found, imported, waiting, errorCode)
        {
            Queued = queued,
            MoreAvailable = more,
            MaybeMore = maybeMore,
        };
    }

    /// <summary>Thrown when a 401 survives one forced refresh: the account needs a reconnect (spec §5.4).</summary>
    private sealed class PlaudReconnectNeededException : Exception
    {
    }

    private async Task<PlaudMcpClient.Session> OpenSessionAsync(Guid userId, string token, CancellationToken ct)
    {
        try
        {
            return await _mcp.OpenSessionAsync(token, ct);
        }
        catch (PlaudMcpException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // One forced (locked) refresh, then give up for this run; a dead grant flips needs_reconnect.
            var refreshed = await _oauth.GetAccessTokenAsync(userId, true, ct);
            if (refreshed == null) throw;
            try
            {
                return await _mcp.OpenSessionAsync(refreshed, ct);
            }
            catch (PlaudMcpException again) when (again.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Spec §5.4: 401 after one refresh → needs_reconnect = 1, state = reconnect_needed, polling stops.
                await MarkAccountNeedsReconnectAsync(userId);
                throw new PlaudReconnectNeededException();
            }
        }
    }

    private async Task MarkAccountNeedsReconnectAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
        var envHost = _envHost;
        var n = await db.PlaudAccounts
            .Where(a => a.UserId == userId && a.OAuthEnv == envHost)
            .ExecuteUpdateAsync(u => u
                .SetProperty(a => a.NeedsReconnect, true)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow), CancellationToken.None);
        if (n > 0)
            _logger.LogWarning("[PlaudSync] user={UserId} reconnect needed: 401 after refresh", userId);
    }

    private async Task HousekeepAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        // A run that died mid-import: reclaimable after 15 min.
        var reclaimBefore = now - ImportingReclaimAfter;
        await db.PlaudImports
            .Where(r => r.UserId == userId && r.Status == PlaudImportStatus.Importing
                        && (r.ClaimedAt == null || r.ClaimedAt < reclaimBefore))
            .ExecuteUpdateAsync(u => u
                .SetProperty(r => r.Status, PlaudImportStatus.Queued)
                .SetProperty(r => r.ClaimedAt, (DateTime?)null)
                .SetProperty(r => r.UpdatedAt, now), ct);

        // pending_audio is retried until 48 h after recorded_at, then no_audio (terminal).
        var giveUpBefore = now - PendingAudioGiveUpAfter;
        await db.PlaudImports
            .Where(r => r.UserId == userId && r.Status == PlaudImportStatus.PendingAudio
                        && (r.RecordedAt ?? r.FirstSeenAt) < giveUpBefore)
            .ExecuteUpdateAsync(u => u
                .SetProperty(r => r.Status, PlaudImportStatus.NoAudio)
                .SetProperty(r => r.ErrorCode, "no_audio_48h")
                .SetProperty(r => r.UpdatedAt, now), ct);

        // Backstop (spec §4.5): fk ON DELETE SET NULL left an imported/submit_failed row without its meeting,
        // so the meeting was deleted by some path other than RemoveMeetingAsync. Treat as a tombstone.
        await db.PlaudImports
            .Where(r => r.UserId == userId && r.MeetingId == null
                        && (r.Status == PlaudImportStatus.Imported || r.Status == PlaudImportStatus.SubmitFailed))
            .ExecuteUpdateAsync(u => u
                .SetProperty(r => r.Status, PlaudImportStatus.DeletedByUser)
                .SetProperty(r => r.DeletedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);
    }

    /// <summary>submit_failed: the meeting and audio exist; retry the Batch submit only (max 3 attempts).</summary>
    private async Task<int> RetrySubmitFailedAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var due = await db.PlaudImports
            .Where(r => r.UserId == userId && r.Status == PlaudImportStatus.SubmitFailed && r.MeetingId != null
                        && (r.NextAttemptAt == null || r.NextAttemptAt <= now))
            .OrderBy(r => r.RecordedAt ?? r.FirstSeenAt)
            .ToListAsync(ct);
        var ok = 0;
        foreach (var row in due)
        {
            var meetingId = row.MeetingId!.Value;
            try
            {
                await _meetingService.SubmitTranscriptionJobAsync(meetingId);
                row.Status = PlaudImportStatus.Imported;
                row.ErrorCode = null;
                row.NextAttemptAt = null;
                row.ImportedAt = DateTime.UtcNow;
                ok++;
            }
            catch (Exception ex)
            {
                row.Attempts++;
                row.ErrorCode = "submit_failed";
                if (row.Attempts >= MaxSubmitAttempts)
                {
                    // Terminal: the meeting goes Failed and the ledger row becomes failed (attempts maxed).
                    row.Status = PlaudImportStatus.Failed;
                    row.NextAttemptAt = null;
                    var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == meetingId && m.CreatedBy == userId, ct);
                    if (meeting != null)
                    {
                        meeting.Status = MeetingStatus.Failed;
                        meeting.LastFailureReason = "transcription_submit_failed";
                        meeting.ErrorMessage = "Transcription could not be started";
                        meeting.UpdatedAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    row.NextAttemptAt = DateTime.UtcNow.AddMinutes(_options.RetryBaseMinutes * Math.Pow(2, row.Attempts));
                }
                _logger.LogWarning("[PlaudSync] user={UserId} file={FileId} meeting={MeetingId} submit retry failed attempts={Attempts} error={Error}",
                    userId, row.PlaudFileId, meetingId, row.Attempts, ex.GetType().Name);
            }
            row.UpdatedAt = DateTime.UtcNow;
        }
        if (due.Count > 0)
            await db.SaveChangesAsync(CancellationToken.None);
        return ok;
    }

    /// <summary>Lists the window and inserts unseen file ids. Existing rows (any status) are left untouched.</summary>
    private async Task<int> ListWindowAsync(PlaudMcpClient.Session session, Guid userId, FirmPlaudSync sync,
        string trigger, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (sync.SyncFrom == null)
        {
            var days = FirmPlaudSync.BackfillDayChoices.Contains(sync.BackfillDays) ? sync.BackfillDays : FirmPlaudSync.DefaultBackfillDays;
            sync.SyncFrom = now.AddDays(-days);
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            await db.PlaudSyncs.Where(s => s.UserId == userId && s.SyncFrom == null)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.SyncFrom, sync.SyncFrom).SetProperty(s => s.UpdatedAt, now), ct);
        }
        var syncFrom = sync.SyncFrom.Value;
        var from = syncFrom;
        if (sync.LastSuccessAt is { } lastSuccess && lastSuccess.AddDays(-2) > from)
            from = lastSuccess.AddDays(-2);
        // date_from/date_to are date-granular with an unverified timezone: widen by a day each side, then
        // filter on the parsed timestamp so the first window is exactly sync_from onwards.
        var dateFrom = DateOnly.FromDateTime(from.AddDays(-1));
        var dateTo = DateOnly.FromDateTime(now.AddDays(1));

        var listed = new List<PlaudFileInfo>();
        for (var page = 1; page <= Math.Max(1, _options.MaxListPages); page++)
        {
            var (files, full) = await _mcp.ListFilesAsync(session, dateFrom, dateTo, page, PageSize, ct);
            listed.AddRange(files);
            if (!full) break;
        }

        var candidates = listed
            .Where(f => (f.StartAt ?? f.CreatedAt) is not { } at || at >= syncFrom)
            .GroupBy(f => f.Id).Select(g => g.First())
            .ToList();
        if (candidates.Count == 0)
            return 0;

        var unseen = await FilterUnseenAsync(userId, candidates, ct);
        return await InsertLedgerRowsAsync(userId, unseen, trigger, ct);
    }

    /// <summary>The candidates whose file id has no ledger row yet (any status counts as seen).</summary>
    private async Task<List<PlaudFileInfo>> FilterUnseenAsync(Guid userId, List<PlaudFileInfo> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
            return candidates;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ids = candidates.Select(f => f.Id).ToList();
        var seen = (await db.PlaudImports.Where(r => r.UserId == userId && ids.Contains(r.PlaudFileId))
            .Select(r => r.PlaudFileId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        return candidates.Where(f => !seen.Contains(f.Id)).ToList();
    }

    /// <summary>
    /// WI #9135 — "Import older…" (spec §5.3): lists the one-off window [from, to) and queues at most
    /// <see cref="ImportOlderPerClick"/> (25) unseen recordings, oldest first, as trigger_kind = 'backfill'.
    /// Returns N found in the window, M queued, K more unseen (listed, not queued) and whether the listing
    /// stopped at its page cap (more may exist).
    /// </summary>
    private async Task<(int Found, int Queued, int More, bool MaybeMore)> ListOlderWindowAsync(PlaudMcpClient.Session session,
        Guid userId, DateTime from, DateTime to, CancellationToken ct)
    {
        var dateFrom = DateOnly.FromDateTime(from.AddDays(-1));
        var dateTo = DateOnly.FromDateTime(to.AddDays(1));
        var inWindow = new Dictionary<string, PlaudFileInfo>(StringComparer.Ordinal);
        var unseen = new Dictionary<string, PlaudFileInfo>(StringComparer.Ordinal);
        var maybeMore = false;
        for (var page = 1; page <= ImportOlderMaxPages; page++)
        {
            var (files, full) = await _mcp.ListFilesAsync(session, dateFrom, dateTo, page, PageSize, ct);
            var fresh = files
                .Where(f => (f.StartAt ?? f.CreatedAt) is { } at && at >= from && at < to)
                .Where(f => inWindow.TryAdd(f.Id, f))
                .ToList();
            foreach (var f in await FilterUnseenAsync(userId, fresh, ct))
                unseen.TryAdd(f.Id, f);
            if (!full)
                break;
            // Enough to fill this click, or out of pages: anything further is for the next click.
            if (unseen.Count > ImportOlderPerClick || page == ImportOlderMaxPages)
            {
                maybeMore = true;
                break;
            }
        }

        var toQueue = unseen.Values
            .OrderBy(f => f.StartAt ?? f.CreatedAt)
            .Take(ImportOlderPerClick)
            .ToList();
        var queued = await InsertLedgerRowsAsync(userId, toQueue, PlaudSyncTrigger.Backfill, ct);
        return (inWindow.Count, queued, unseen.Count - toQueue.Count, maybeMore);
    }

    /// <summary>Inserts queued (or too_long) ledger rows; a concurrent insert of the same file id is left alone.</summary>
    private async Task<int> InsertLedgerRowsAsync(Guid userId, List<PlaudFileInfo> files, string trigger, CancellationToken ct)
    {
        if (files.Count == 0)
            return 0;
        await using var ldb = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var maxMs = (long)_options.MaxDurationMinutes * 60_000L;
        var added = 0;
        foreach (var f in files)
        {
            var tooLong = f.DurationMs is long ms && ms > maxMs;
            var row = new FirmPlaudImport
            {
                UserId = userId,
                PlaudFileId = f.Id,
                Title = Truncate(f.Name, 500),
                RecordedAt = f.StartAt ?? f.CreatedAt,
                DurationMs = f.DurationMs,
                // Over MaxDurationMinutes: too_long straight from the listing, never downloaded.
                Status = tooLong ? PlaudImportStatus.TooLong : PlaudImportStatus.Queued,
                ErrorCode = tooLong ? "over_max_duration" : null,
                TriggerKind = trigger,
                FirstSeenAt = now,
                UpdatedAt = now,
            };
            ldb.PlaudImports.Add(row);
            try
            {
                await ldb.SaveChangesAsync(ct);
                added++;
                _logger.LogInformation("[PlaudSync] user={UserId} file={FileId} durationMs={DurationMs} status={Status}",
                    userId, f.Id, f.DurationMs, row.Status);
            }
            catch (DbUpdateException ex) when (CalendarAutoSyncService.IsDuplicateKeyException(ex))
            {
                // A concurrent run inserted it first (uk_fpi_user_file): leave that row alone.
                ldb.Entry(row).State = EntityState.Detached;
            }
        }
        return added;
    }

    /// <summary>
    /// Imports retryable rows oldest-first, live (auto/manual) rows before backfill rows so a new recording
    /// does not wait behind an "Import older" queue. Caps are checked before each get_file. A 429 is rethrown
    /// (after the row is put back) so the run records Retry-After.
    /// </summary>
    private async Task<int> ImportQueuedAsync(PlaudMcpClient.Session session, Guid userId,
        string entraOid, string trigger, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        List<FirmPlaudImport> rows;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            // Only retryable states are ever picked up: queued, pending_audio, and failed rows that are due
            // and under the attempt limit. submit_failed is retried by RetrySubmitFailedAsync (submit only).
            rows = await db.PlaudImports.AsNoTracking()
                .Where(r => r.UserId == userId
                            && (r.Status == PlaudImportStatus.Queued
                                || r.Status == PlaudImportStatus.PendingAudio
                                || (r.Status == PlaudImportStatus.Failed && r.Attempts < MaxFailedAttempts
                                    && r.NextAttemptAt != null && r.NextAttemptAt <= now)))
                .OrderBy(r => r.TriggerKind == PlaudSyncTrigger.Backfill ? 1 : 0)
                .ThenBy(r => r.RecordedAt ?? r.FirstSeenAt)
                .Take(200)
                .ToListAsync(ct);
        }

        var maxPerRun = Math.Max(0, _options.MaxImportsPerRun);
        var maxPerUserLive = Math.Max(0, _options.MaxInFlightPerUser);
        var maxGlobal = Math.Max(0, _options.MaxInFlightGlobal);
        var maxMs = (long)_options.MaxDurationMinutes * 60_000L;

        int attempted = 0, imported = 0;
        foreach (var row in rows)
        {
            // Duration cap first: no claim, no get_file, no download, does not use a slot.
            if (row.DurationMs is long ms && ms > maxMs)
            {
                await SetStatusAsync(row.Id, row.Status, PlaudImportStatus.TooLong, "over_max_duration", ct);
                continue;
            }

            // Caps, all before get_file.
            if (attempted >= maxPerRun)
                break;
            // spec §8.2: 1 in flight per user while the user's queue is a backfill (row or run).
            var maxPerUser = trigger == PlaudSyncTrigger.Backfill || row.TriggerKind == PlaudSyncTrigger.Backfill
                ? Math.Min(maxPerUserLive, 1)
                : maxPerUserLive;
            var (userInFlight, globalInFlight) = await CountInFlightAsync(userId, ct);
            if (userInFlight >= maxPerUser || globalInFlight >= maxGlobal)
            {
                _logger.LogInformation("[PlaudSync] user={UserId} capped: inFlightUser={User}/{MaxUser} inFlightGlobal={Global}/{MaxGlobal}",
                    userId, userInFlight, maxPerUser, globalInFlight, maxGlobal);
                break;
            }

            // Claim the ledger row (conditional on its current status).
            if (!await ClaimRowAsync(row.Id, row.Status, ct))
                continue;
            attempted++;

            var outcome = await ImportOneAsync(session, row, entraOid, ct);
            if (outcome == ImportOutcome.Imported)
                imported++;
        }
        return imported;
    }

    private enum ImportOutcome { Imported, NotImported }

    private async Task<ImportOutcome> ImportOneAsync(PlaudMcpClient.Session session, FirmPlaudImport row,
        string entraOid, CancellationToken ct)
    {
        var userId = row.UserId;
        var fileId = row.PlaudFileId;
        string? tempPath = null;
        try
        {
            // get_file → presigned URL into a local variable only (spec §4.7, §9.3).
            var payload = await _mcp.GetFilePayloadAsync(session, fileId, ct);
            var presignedUrl = PlaudMcpClient.ReadPresignedUrl(payload);
            if (presignedUrl == null)
            {
                await FinishRowAsync(row.Id, PlaudImportStatus.PendingAudio, "no_presigned_url", null, countAttempt: false, ct);
                _logger.LogInformation("[PlaudSync] user={UserId} file={FileId} status=pending_audio", userId, fileId);
                return ImportOutcome.NotImported;
            }

            var recordedAt = row.RecordedAt
                             ?? PlaudMcpClient.ParseTimestamp(FindNode(payload, "start_at"))
                             ?? PlaudMcpClient.ParseTimestamp(FindNode(payload, "created_at"))
                             ?? row.FirstSeenAt;
            var durationMs = row.DurationMs ?? ParseLongNode(FindNode(payload, "duration"));
            if (durationMs is long ms && ms > (long)_options.MaxDurationMinutes * 60_000L)
            {
                await FinishRowAsync(row.Id, PlaudImportStatus.TooLong, "over_max_duration", null, countAttempt: false, ct);
                return ImportOutcome.NotImported;
            }

            tempPath = Path.Combine(Path.GetTempPath(), $"plaud-{Guid.NewGuid():N}.audio");
            var download = await DownloadToFileAsync(presignedUrl, tempPath, ct);
            if (download.Result == DownloadResult.Expired)
            {
                // A 403/404 on the URL means it expired: re-call get_file once.
                payload = await _mcp.GetFilePayloadAsync(session, fileId, ct);
                presignedUrl = PlaudMcpClient.ReadPresignedUrl(payload);
                download = presignedUrl == null
                    ? new DownloadOutcome(DownloadResult.Error, 0, null, null)
                    : await DownloadToFileAsync(presignedUrl, tempPath, ct);
            }
            presignedUrl = null;
            payload = null;

            switch (download.Result)
            {
                case DownloadResult.TooLarge:
                    await FinishRowAsync(row.Id, PlaudImportStatus.TooLong, "over_max_bytes", null, countAttempt: false, ct);
                    _logger.LogInformation("[PlaudSync] user={UserId} file={FileId} status=too_long bytes>{Max}", userId, fileId, _options.MaxAudioBytes);
                    return ImportOutcome.NotImported;
                case DownloadResult.Expired:
                case DownloadResult.Error:
                    await FinishRowAsync(row.Id, PlaudImportStatus.Failed, download.Result == DownloadResult.Expired ? "audio_url_expired" : "download_failed",
                        null, countAttempt: true, ct);
                    return ImportOutcome.NotImported;
            }

            var request = new IngestRequest(
                CreatedBy: userId,
                CreatorEntraOid: entraOid,
                Source: Source,
                Platform: Source,
                Title: string.IsNullOrWhiteSpace(row.Title) ? "Plaud recording" : row.Title!,
                RecordedAtUtc: DateTime.SpecifyKind(recordedAt, DateTimeKind.Utc),
                DurationSeconds: durationMs.HasValue ? (int)Math.Min(int.MaxValue, durationMs.Value / 1000) : null,
                ExternalId: "plaud:" + fileId,
                FileExtension: download.Extension);

            IngestResult result;
            await using (var audio = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
            {
                try
                {
                    result = await _ingest.IngestRecordingAsync(request, audio, download.Bytes, download.ContentType ?? "audio/mp4", ct);
                }
                catch (MeetingIngestStorageException ex)
                {
                    // The meeting row has no audio. Remove it (not a user delete, so no tombstone) so a retry
                    // can create it again; otherwise the external_id dedupe would turn the retry into a no-op.
                    await DeleteAudiolessMeetingAsync(ex.MeetingId, userId);
                    await FinishRowAsync(row.Id, PlaudImportStatus.Failed, "s3_upload_failed", null, countAttempt: true, ct);
                    return ImportOutcome.NotImported;
                }
            }

            if (result.AlreadyExisted || result.Submitted)
            {
                await FinishRowAsync(row.Id, PlaudImportStatus.Imported, null, result.MeetingId, countAttempt: false, ct);
                _logger.LogInformation("[PlaudSync] user={UserId} file={FileId} meeting={MeetingId} bytes={Bytes} durationMs={DurationMs} status=imported{Existing}",
                    userId, fileId, result.MeetingId, download.Bytes, durationMs, result.AlreadyExisted ? " (already existed)" : "");
                if (!result.AlreadyExisted)
                    await ArchivePlaudTextAsync(session, row.Id, fileId, result.MeetingId, ct);
                return ImportOutcome.Imported;
            }

            // Meeting and audio exist; the Batch submit threw. The next run retries the submit.
            await FinishRowAsync(row.Id, PlaudImportStatus.SubmitFailed, "submit_failed", result.MeetingId, countAttempt: true, ct,
                nextAttemptAt: DateTime.UtcNow);
            _logger.LogWarning("[PlaudSync] user={UserId} file={FileId} meeting={MeetingId} status=submit_failed", userId, fileId, result.MeetingId);
            return ImportOutcome.NotImported;
        }
        catch (PlaudMcpException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Not the recording's fault: put it back and stop the run; RunAsync records Retry-After.
            await SetStatusAsync(row.Id, PlaudImportStatus.Importing, row.Status, null, CancellationToken.None);
            _logger.LogWarning("[PlaudSync] 429 user={UserId} file={FileId}", userId, fileId);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var code = ex is PlaudMcpException pe
                ? (pe.StatusCode.HasValue ? $"plaud_{(int)pe.StatusCode.Value}" : "plaud_error")
                : "import_error";
            await FinishRowAsync(row.Id, PlaudImportStatus.Failed, code, null, countAttempt: true, CancellationToken.None);
            // Exception type only: messages from HTTP or Plaud could carry a URL or user data.
            _logger.LogWarning("[PlaudSync] user={UserId} file={FileId} status=failed code={Code} error={Error}",
                userId, fileId, code, ex.GetType().Name);
            return ImportOutcome.NotImported;
        }
        finally
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); }
                catch (Exception ex)
                {
                    _logger.LogWarning("[PlaudSync] temp file delete failed: {Error}", ex.GetType().Name);
                }
            }
        }
    }

    // ── Download (spec §4.7 step 3) ──────────────────────────────────────────────────────────────

    private enum DownloadResult { Ok, Expired, TooLarge, Error }

    private sealed record DownloadOutcome(DownloadResult Result, long Bytes, string? ContentType, string? Extension);

    /// <summary>
    /// Streams the presigned URL through the logging-free "PlaudAudio" client into <paramref name="path"/>,
    /// aborting past MaxAudioBytes. The URL is a parameter only: never stored, returned or logged.
    /// </summary>
    private async Task<DownloadOutcome> DownloadToFileAsync(string presignedUrl, string path, CancellationToken ct)
    {
        var maxBytes = Math.Max(1, _options.MaxAudioBytes);
        try
        {
            var http = _httpClientFactory.CreateClient(AudioHttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, presignedUrl);
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                return new DownloadOutcome(DownloadResult.Expired, 0, null, null);
            if (!res.IsSuccessStatusCode)
                return new DownloadOutcome(DownloadResult.Error, 0, null, null);
            if (res.Content.Headers.ContentLength is long declared && declared > maxBytes)
                return new DownloadOutcome(DownloadResult.TooLarge, declared, null, null);

            var contentType = res.Content.Headers.ContentType?.MediaType;
            var ext = ExtensionFor(contentType) ?? ExtensionFromPath(new Uri(presignedUrl).AbsolutePath) ?? ".m4a";

            long total = 0;
            await using (var src = await res.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                        return new DownloadOutcome(DownloadResult.TooLarge, total, contentType, ext);
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            if (total == 0)
                return new DownloadOutcome(DownloadResult.Error, 0, contentType, ext);
            var storedType = contentType is null or "application/octet-stream" or "binary/octet-stream"
                ? ContentTypeFor(ext)
                : contentType;
            return new DownloadOutcome(DownloadResult.Ok, total, storedType, ext);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("[PlaudSync] audio download failed: {Error}", ex.GetType().Name);
            return new DownloadOutcome(DownloadResult.Error, 0, null, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[PlaudSync] audio download timed out");
            return new DownloadOutcome(DownloadResult.Error, 0, null, null);
        }
    }

    private static string? ExtensionFor(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "audio/mp4" or "audio/m4a" or "audio/x-m4a" or "audio/mp4a-latm" => ".m4a",
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/ogg" => ".ogg",
        "audio/opus" => ".opus",
        "audio/wav" or "audio/x-wav" or "audio/wave" => ".wav",
        "audio/webm" => ".webm",
        "audio/aac" or "audio/x-aac" => ".aac",
        "audio/flac" or "audio/x-flac" => ".flac",
        _ => null,
    };

    private static string ContentTypeFor(string ext) => ext switch
    {
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".opus" => "audio/opus",
        ".wav" => "audio/wav",
        ".webm" => "audio/webm",
        ".aac" => "audio/aac",
        ".flac" => "audio/flac",
        _ => "audio/mp4",
    };

    private static string? ExtensionFromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".m4a" or ".mp3" or ".ogg" or ".opus" or ".wav" or ".webm" or ".aac" or ".flac" or ".mp4"
            ? (ext == ".mp4" ? ".m4a" : ext)
            : null;
    }

    // ── D17 archive: Plaud transcript and note next to the audio ─────────────────────────────────

    /// <summary>
    /// Best-effort: never throws, never blocks the import (the row is already imported), and the content is
    /// never shown, pushed or logged. Writes firm-audio/{meetingId}/plaud-transcript.json and plaud-note.md.
    /// </summary>
    private async Task ArchivePlaudTextAsync(PlaudMcpClient.Session session, long rowId, string fileId, long meetingId,
        CancellationToken ct)
    {
        var transcriptOk = false;
        var noteOk = false;
        try
        {
            var pages = new JsonArray();
            string? cursor = null;
            for (var i = 0; i < MaxTranscriptPages; i++)
            {
                var (text, next) = await _mcp.GetTranscriptPageAsync(session, fileId, cursor, 500, ct);
                if (!string.IsNullOrEmpty(text))
                    pages.Add(text);
                if (next == null || next == cursor)
                    break;
                cursor = next;
            }
            if (pages.Count > 0)
            {
                var doc = new JsonObject
                {
                    ["plaud_file_id"] = fileId,
                    ["block"] = "transaction",
                    ["fetched_at"] = DateTime.UtcNow.ToString("o"),
                    ["pages"] = pages,
                };
                await _s3.UploadTextAsync($"firm-audio/{meetingId}/plaud-transcript.json", doc.ToJsonString(), "application/json");
                transcriptOk = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation("[PlaudSync] file={FileId} meeting={MeetingId} archive transcript skipped: {Error}",
                fileId, meetingId, ex.GetType().Name);
        }

        try
        {
            var note = await _mcp.GetNoteTextAsync(session, fileId, ct);
            if (!string.IsNullOrEmpty(note))
            {
                await _s3.UploadTextAsync($"firm-audio/{meetingId}/plaud-note.md", note, "text/markdown");
                noteOk = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation("[PlaudSync] file={FileId} meeting={MeetingId} archive note skipped: {Error}",
                fileId, meetingId, ex.GetType().Name);
        }

        try
        {
            var status = transcriptOk && noteOk ? "ok" : transcriptOk || noteOk ? "partial" : "failed";
            await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
            await db.PlaudImports.Where(r => r.Id == rowId)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.PlaudArchiveStatus, status), CancellationToken.None);
        }
        catch (Exception)
        {
            // Flag only.
        }
    }

    // ── Ledger helpers ───────────────────────────────────────────────────────────────────────────

    private async Task<bool> ClaimRowAsync(long rowId, string expectedStatus, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var n = await db.PlaudImports
            .Where(r => r.Id == rowId && r.Status == expectedStatus)
            .ExecuteUpdateAsync(u => u
                .SetProperty(r => r.Status, PlaudImportStatus.Importing)
                .SetProperty(r => r.ClaimedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);
        return n == 1;
    }

    private async Task SetStatusAsync(long rowId, string fromStatus, string toStatus, string? errorCode, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        await db.PlaudImports
            .Where(r => r.Id == rowId && r.Status == fromStatus)
            .ExecuteUpdateAsync(u => u
                .SetProperty(r => r.Status, toStatus)
                .SetProperty(r => r.ErrorCode, errorCode)
                .SetProperty(r => r.ClaimedAt, (DateTime?)null)
                .SetProperty(r => r.UpdatedAt, now), ct);
    }

    /// <summary>Ends an import attempt. Failed rows back off base × 2^attempts and stop after 3 attempts.</summary>
    private async Task FinishRowAsync(long rowId, string status, string? errorCode, long? meetingId, bool countAttempt,
        CancellationToken ct, DateTime? nextAttemptAt = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
        var row = await db.PlaudImports.FirstOrDefaultAsync(r => r.Id == rowId, CancellationToken.None);
        if (row == null)
            return;
        var now = DateTime.UtcNow;
        if (countAttempt)
            row.Attempts++;
        row.Status = status;
        row.ErrorCode = errorCode;
        row.ClaimedAt = null;
        row.UpdatedAt = now;
        if (meetingId.HasValue)
            row.MeetingId = meetingId;
        if (status == PlaudImportStatus.Imported)
        {
            row.ImportedAt = now;
            row.NextAttemptAt = null;
        }
        else if (status == PlaudImportStatus.Failed)
        {
            row.NextAttemptAt = row.Attempts < MaxFailedAttempts
                ? now.AddMinutes(_options.RetryBaseMinutes * Math.Pow(2, row.Attempts))
                : null;
        }
        else
        {
            row.NextAttemptAt = nextAttemptAt;
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task DeleteAudiolessMeetingAsync(long meetingId, Guid userId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
            await db.Meetings
                .Where(m => m.Id == meetingId && m.CreatedBy == userId && m.Source == Source && m.AudioS3Key == null)
                .ExecuteDeleteAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[PlaudSync] meeting={MeetingId} cleanup after storage failure failed: {Error}", meetingId, ex.GetType().Name);
        }
    }

    private async Task<(int User, int Global)> CountInFlightAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var inFlight = db.Meetings.Where(m => m.Source == Source
            && (m.Status == MeetingStatus.Transcribing || m.Status == MeetingStatus.Summarizing));
        var global = await inFlight.CountAsync(ct);
        var user = await inFlight.CountAsync(m => m.CreatedBy == userId, ct);
        return (user, global);
    }

    private async Task<int> CountWaitingAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PlaudImports.CountAsync(r => r.UserId == userId && PlaudImportStatus.Waiting.Contains(r.Status), ct);
    }

    private async Task<FirmPlaudSync> EnsureSyncRowAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.PlaudSyncs.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (row != null)
            return row;
        row = new FirmPlaudSync { UserId = userId, UpdatedAt = DateTime.UtcNow };
        db.PlaudSyncs.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return row;
        }
        catch (DbUpdateException ex) when (CalendarAutoSyncService.IsDuplicateKeyException(ex))
        {
            await using var db2 = await _dbFactory.CreateDbContextAsync(ct);
            return await db2.PlaudSyncs.AsNoTracking().FirstAsync(s => s.UserId == userId, ct);
        }
    }

    private async Task<int> CountQueuedAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PlaudImports.CountAsync(r => r.UserId == userId && r.Status == PlaudImportStatus.Queued, ct);
    }

    /// <summary>Writes the run's status and schedules the next auto run (spec §5.3, §5.4).</summary>
    private async Task WriteRunStatusAsync(Guid userId, string state, string? errorCode, bool success, int importedCount,
        TimeSpan? retryAfter, bool backlog, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
        var row = await db.PlaudSyncs.FirstOrDefaultAsync(s => s.UserId == userId, CancellationToken.None);
        if (row == null)
            return;
        var now = DateTime.UtcNow;
        row.State = state;
        row.LastErrorCode = errorCode;
        row.LastCheckedAt = now;
        row.UpdatedAt = now;
        if (success)
        {
            row.ConsecutiveFailures = 0;
            row.LastSuccessAt = now;
        }
        else
        {
            row.ConsecutiveFailures++;
        }
        if (importedCount > 0)
        {
            row.LastImportAt = now;
            row.LastImportedCount = importedCount;
        }
        row.NextSyncAt = ComputeNextSyncAt(row.AutoSync, row.IntervalMinutes, state, row.ConsecutiveFailures,
            retryAfter, backlog, now);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// The next auto run (spec §5.3, §5.4). Null when auto-sync is off or the state stops polling.
    ///   ok/idle:      now + interval × U(0.9, 1.1); now + 2 min while queued rows remain (backlog).
    ///   backoff:      now + min(interval × 2^n, 6 h), n = consecutive failures.
    ///   rate_limited: now + max(Retry-After, min(interval × 2^n, 6 h)); Retry-After capped at 24 h.
    /// The interval is clamped to [10, 1440] whatever the stored value.
    /// </summary>
    public static DateTime? ComputeNextSyncAt(bool autoSync, int intervalMinutes, string state, int consecutiveFailures,
        TimeSpan? retryAfter, bool backlog, DateTime now)
    {
        if (!autoSync || NotPolledStates.Contains(state))
            return null;
        var interval = TimeSpan.FromMinutes(ClampInterval(intervalMinutes));
        switch (state)
        {
            case StateBackoff:
                return now + Backoff(interval, consecutiveFailures);
            case StateRateLimited:
            {
                var backoff = Backoff(interval, consecutiveFailures);
                var ra = retryAfter is { } r ? (r > MaxRetryAfter ? MaxRetryAfter : r) : TimeSpan.Zero;
                return now + (ra > backoff ? ra : backoff);
            }
            default:
                return backlog ? now + BacklogRecheck : now + Jittered(interval);
        }
    }

    /// <summary>min(interval × 2^n, 6 h).</summary>
    public static TimeSpan Backoff(TimeSpan interval, int n)
    {
        var exponent = Math.Clamp(n, 0, 20);
        var minutes = interval.TotalMinutes * Math.Pow(2, exponent);
        return minutes >= MaxBackoff.TotalMinutes ? MaxBackoff : TimeSpan.FromMinutes(minutes);
    }

    /// <summary>±10% jitter: interval × U(0.9, 1.1).</summary>
    private static TimeSpan Jittered(TimeSpan interval) =>
        TimeSpan.FromSeconds(interval.TotalSeconds * (0.9 + 0.2 * Random.Shared.NextDouble()));

    /// <summary>First run after enabling: now + U(0, 2 min).</summary>
    private static DateTime FirstRunAt(DateTime now) =>
        now + TimeSpan.FromSeconds(FirstRunSpread.TotalSeconds * Random.Shared.NextDouble());

    private async Task MoveSyncFromBackAsync(Guid userId, DateTime from, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.PlaudSyncs
            .Where(s => s.UserId == userId && (s.SyncFrom == null || s.SyncFrom > from))
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.SyncFrom, from)
                .SetProperty(s => s.UpdatedAt, DateTime.UtcNow), ct);
    }

    /// <summary>Background safety net: a run that threw outside RunAsync still backs off instead of retrying every tick.</summary>
    public async Task RecordRunErrorAsync(Guid userId, string errorCode)
    {
        try
        {
            await WriteRunStatusAsync(userId, StateBackoff, errorCode, success: false, importedCount: 0, retryAfter: null,
                backlog: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[PlaudSync] user={UserId} status write failed: {Error}", userId, ex.GetType().Name);
        }
    }

    // ── Auto-sync settings (WI #9135, spec §6.1) ─────────────────────────────────────────────────

    /// <summary>
    /// "Automatically sync my Plaud recordings". On: first run at now + U(0, 2 min) (sync_from is set by that
    /// first run from "On first sync, import the last", so the window stays choosable until then). Off: clears
    /// next_sync_at, so the poller never selects the user.
    /// </summary>
    public async Task SetAutoSyncAsync(Guid userId, bool enabled, CancellationToken ct = default)
    {
        await EnsureSyncRowAsync(userId, ct);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var next = enabled ? FirstRunAt(now) : (DateTime?)null;
        await db.PlaudSyncs.Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.AutoSync, enabled)
                .SetProperty(s => s.NextSyncAt, next)
                .SetProperty(s => s.UpdatedAt, now), ct);
        _logger.LogInformation("[PlaudSync] user={UserId} auto_sync={AutoSync}", userId, enabled);
    }

    /// <summary>"Check every": clamped to [10, 1440]; returns the stored value. Pulls next_sync_at in if it is later than one new interval.</summary>
    public async Task<int> SetIntervalAsync(Guid userId, int minutes, CancellationToken ct = default)
    {
        var clamped = ClampInterval(minutes);
        await EnsureSyncRowAsync(userId, ct);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.PlaudSyncs.FirstAsync(s => s.UserId == userId, ct);
        var now = DateTime.UtcNow;
        row.IntervalMinutes = clamped;
        var interval = TimeSpan.FromMinutes(clamped);
        if (row.AutoSync && row.State is StateOk or StateIdle
                         && (row.NextSyncAt == null || row.NextSyncAt > now + interval * 1.1))
            row.NextSyncAt = now + Jittered(interval);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return clamped;
    }

    /// <summary>
    /// Called after a successful OAuth callback. D13: auto-sync is on by default on first connect (a sync row
    /// that has never run and was never switched on). A reconnect clears reconnect_needed / disconnected so
    /// polling resumes, keeping the user's own auto-sync choice.
    /// </summary>
    public async Task OnAccountConnectedAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var row = await db.PlaudSyncs.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (row == null)
        {
            db.PlaudSyncs.Add(new FirmPlaudSync
            {
                UserId = userId,
                AutoSync = true,
                IntervalMinutes = DefaultIntervalMinutes,
                NextSyncAt = FirstRunAt(now),
                State = StateIdle,
                UpdatedAt = now,
            });
            try
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("[PlaudSync] user={UserId} connected: auto_sync=True (default)", userId);
                return;
            }
            catch (DbUpdateException ex) when (CalendarAutoSyncService.IsDuplicateKeyException(ex))
            {
                db.ChangeTracker.Clear();
                row = await db.PlaudSyncs.FirstAsync(s => s.UserId == userId, ct);
            }
        }

        if (!row.AutoSync && row.LastCheckedAt == null && row.SyncFrom == null)
            row.AutoSync = true; // never used: first connect (D13)
        if (NotPolledStates.Contains(row.State) || row.State is StateBackoff or StateRateLimited)
        {
            row.State = StateIdle;
            row.ConsecutiveFailures = 0;
            row.LastErrorCode = null;
        }
        row.NextSyncAt = row.AutoSync ? FirstRunAt(now) : null;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("[PlaudSync] user={UserId} connected: auto_sync={AutoSync}", userId, row.AutoSync);
    }

    /// <summary>Called after disconnect: state = disconnected, polling stops. The auto-sync choice and ledger are kept.</summary>
    public async Task OnAccountDisconnectedAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.PlaudSyncs.Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.State, StateDisconnected)
                .SetProperty(s => s.NextSyncAt, (DateTime?)null)
                .SetProperty(s => s.UpdatedAt, DateTime.UtcNow), ct);
    }

    /// <summary>
    /// "Import older…" (spec §5.3): a one-off window [picked date, sync_from) queued as backfill, 25 listed per
    /// click, under the same claim as Sync now. The picked date is a UTC calendar date, at most 365 days back.
    /// </summary>
    public async Task<PlaudSyncRunResult> ImportOlderAsync(Guid userId, DateOnly fromDate, CancellationToken ct = default)
    {
        var sync = await EnsureSyncRowAsync(userId, ct);
        var now = DateTime.UtcNow;
        var earliest = DateOnly.FromDateTime(now.AddDays(-ImportOlderMaxDaysBack));
        if (fromDate < earliest)
            fromDate = earliest;
        var from = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = sync.SyncFrom ?? now;
        if (from >= to)
            return new PlaudSyncRunResult(false, 0, 0, await CountWaitingAsync(userId, ct), "nothing_older");

        var claimToken = NewClaimToken();
        if (!await WaitForClaimAsync(userId, claimToken, ct))
        {
            _logger.LogInformation("[PlaudSync] user={UserId} import older skipped: already running", userId);
            return PlaudSyncRunResult.Busy;
        }
        try
        {
            return await RunAsync(userId, PlaudSyncTrigger.Backfill, (from, to), ct);
        }
        finally
        {
            await ReleaseUserClaimAsync(userId, claimToken, CancellationToken.None);
        }
    }

    // ── Settings ─────────────────────────────────────────────────────────────────────────────────

    public async Task<PlaudSyncStatus> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var counts = await db.PlaudImports.Where(r => r.UserId == userId)
            .GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Sum(string[] statuses) => counts.Where(c => statuses.Contains(c.Status)).Sum(c => c.Count);
        var sync = await db.PlaudSyncs.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        return new PlaudSyncStatus(
            Imported: Sum(new[] { PlaudImportStatus.Imported }),
            Waiting: Sum(PlaudImportStatus.Waiting),
            Skipped: Sum(PlaudImportStatus.Skipped),
            LastCheckedAt: sync?.LastCheckedAt,
            State: sync?.State ?? "idle",
            LastErrorCode: sync?.LastErrorCode,
            BackfillDays: sync?.BackfillDays ?? FirmPlaudSync.DefaultBackfillDays,
            WindowLocked: sync?.SyncFrom != null)
        {
            AutoSync = sync?.AutoSync ?? false,
            IntervalMinutes = ClampInterval(sync?.IntervalMinutes ?? DefaultIntervalMinutes),
            NextSyncAt = sync?.NextSyncAt,
            LastImportAt = sync?.LastImportAt,
            LastImportedCount = sync?.LastImportedCount ?? 0,
            TooLong = Sum(new[] { PlaudImportStatus.TooLong }),
            NoAudio = Sum(new[] { PlaudImportStatus.NoAudio }),
            SyncFrom = sync?.SyncFrom,
        };
    }

    /// <summary>"On first sync, import the last N days". Ignored once sync_from is set.</summary>
    public async Task<bool> SetBackfillDaysAsync(Guid userId, int days, CancellationToken ct = default)
    {
        if (!FirmPlaudSync.BackfillDayChoices.Contains(days))
            return false;
        await EnsureSyncRowAsync(userId, ct);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var n = await db.PlaudSyncs.Where(s => s.UserId == userId && s.SyncFrom == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.BackfillDays, days).SetProperty(s => s.UpdatedAt, DateTime.UtcNow), ct);
        return n == 1;
    }

    // ── D14: completion notifications for old / backfilled imports ───────────────────────────────

    /// <summary>
    /// Ruling D14: no auto-email PDF and no phone push for a Plaud import that was an explicit backfill or
    /// whose recording was more than 24 h old when imported. The FAIT KB push is not affected.
    /// </summary>
    public static async Task<bool> SuppressUserNotificationsAsync(FirmDbContext db, FirmMeeting meeting, CancellationToken ct = default)
    {
        if (!string.Equals(meeting.Source, Source, StringComparison.OrdinalIgnoreCase))
            return false;
        var row = await db.PlaudImports.AsNoTracking()
            .Where(r => r.MeetingId == meeting.Id)
            .Select(r => new { r.TriggerKind, r.RecordedAt, r.ImportedAt, r.FirstSeenAt })
            .FirstOrDefaultAsync(ct);
        if (row != null)
        {
            if (row.TriggerKind == PlaudSyncTrigger.Backfill)
                return true;
            var importedAt = row.ImportedAt ?? meeting.CreatedAt;
            return row.RecordedAt is { } rec && importedAt - rec > TimeSpan.FromHours(24);
        }
        var recordedAt = meeting.StartDatetime ?? meeting.StartedAt;
        return recordedAt is { } r2 && meeting.CreatedAt - r2 > TimeSpan.FromHours(24);
    }

    // ── small helpers ────────────────────────────────────────────────────────────────────────────

    private static JsonNode? FindNode(JsonNode? node, string name)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                    if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && value is JsonValue)
                        return value;
                foreach (var (_, value) in obj)
                    if (FindNode(value, name) is { } nested)
                        return nested;
                return null;
            case JsonArray arr:
                foreach (var item in arr)
                    if (FindNode(item, name) is { } nested)
                        return nested;
                return null;
            default:
                return null;
        }
    }

    private static long? ParseLongNode(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        return long.TryParse(v.ToString(), out var p) ? p : null;
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];
}
