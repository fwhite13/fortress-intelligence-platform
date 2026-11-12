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
}

/// <summary>Settings status lines (spec §6.1).</summary>
public record PlaudSyncStatus(int Imported, int Waiting, int Skipped, DateTime? LastCheckedAt, string State,
    string? LastErrorCode, int BackfillDays, bool WindowLocked);

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
    }

    // ── Claim (spec §5.2) ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Settings "Sync now": takes the per-user claim (waiting up to 10 s), runs <see cref="SyncUserAsync"/>
    /// and releases the claim in finally. Returns <see cref="PlaudSyncRunResult.Busy"/> if another run holds it.
    /// </summary>
    public async Task<PlaudSyncRunResult> SyncNowAsync(Guid userId, string trigger = PlaudSyncTrigger.Manual,
        CancellationToken ct = default)
    {
        await EnsureSyncRowAsync(userId, ct);
        // Per-run token (nonce first, so truncation to claimed_by's 64 chars never drops it).
        var claimToken = $"{Guid.NewGuid():N}:{InstanceId}";
        if (claimToken.Length > 64) claimToken = claimToken[..64];
        var deadline = DateTime.UtcNow + SyncNowClaimWait;
        while (!await TryClaimUserAsync(userId, claimToken, ct))
        {
            if (DateTime.UtcNow >= deadline)
            {
                _logger.LogInformation("[PlaudSync] user={UserId} sync now skipped: already running", userId);
                return PlaudSyncRunResult.Busy;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        try
        {
            return await SyncUserAsync(userId, trigger, ct);
        }
        finally
        {
            await ReleaseUserClaimAsync(userId, claimToken);
        }
    }

    /// <summary>The single conditional UPDATE from spec §5.2: ownership is decided by the affected-row count.</summary>
    public async Task<bool> TryClaimUserAsync(Guid userId, string claimToken, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var staleBefore = now - UserClaimStaleAfter;
        var rows = await db.PlaudSyncs
            .Where(s => s.UserId == userId && (s.ClaimedAt == null || s.ClaimedAt < staleBefore))
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.ClaimedBy, claimToken)
                .SetProperty(s => s.ClaimedAt, now)
                .SetProperty(s => s.UpdatedAt, now), ct);
        return rows == 1;
    }

    public async Task ReleaseUserClaimAsync(Guid userId, string claimToken)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
            await db.PlaudSyncs
                .Where(s => s.UserId == userId && s.ClaimedBy == claimToken)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.ClaimedBy, (string?)null)
                    .SetProperty(s => s.ClaimedAt, (DateTime?)null)
                    .SetProperty(s => s.UpdatedAt, DateTime.UtcNow), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The 10-minute stale window frees it anyway.
            _logger.LogWarning("[PlaudSync] user={UserId} claim release failed: {Error}", userId, ex.GetType().Name);
        }
    }

    // ── One run ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>One sync run for a user. The caller must hold the user's claim.</summary>
    public async Task<PlaudSyncRunResult> SyncUserAsync(Guid userId, string trigger, CancellationToken ct = default)
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
            var (state, code) = !status.Connected ? ("disconnected", "not_connected")
                : status.NeedsReconnect ? ("reconnect_needed", "reconnect_needed")
                : status.OtherEnvironment ? ("idle", "other_environment")
                : ("backoff", "token_unavailable");
            await WriteRunStatusAsync(userId, state, code, success: false, importedCount: 0, ct);
            return new PlaudSyncRunResult(false, 0, 0, await CountWaitingAsync(userId, ct), code);
        }

        int found = 0, imported = 0;
        string? errorCode = null;
        try
        {
            var session = await OpenSessionAsync(userId, token, ct);

            // 1. Housekeeping and submit retries.
            await HousekeepAsync(userId, ct);
            imported += await RetrySubmitFailedAsync(userId, ct);

            // 2. List the window.
            found = await ListWindowAsync(session, userId, sync, trigger, ct);

            // 3. Import within the caps.
            var (n, code) = await ImportQueuedAsync(session, userId, user.EntraOid, trigger, ct);
            imported += n;
            errorCode = code;
        }
        catch (PlaudMcpException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            errorCode = "rate_limited";
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

        var finalState = errorCode switch
        {
            null => "ok",
            "rate_limited" => "rate_limited",
            _ => "backoff",
        };
        await WriteRunStatusAsync(userId, finalState, errorCode, success: errorCode == null, imported, ct);
        var waiting = await CountWaitingAsync(userId, ct);
        _logger.LogInformation("[PlaudSync] user={UserId} trigger={Trigger} found={Found} imported={Imported} waiting={Waiting} code={Code}",
            userId, trigger, found, imported, waiting, errorCode ?? "-");
        return new PlaudSyncRunResult(false, found, imported, waiting, errorCode);
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
            return await _mcp.OpenSessionAsync(refreshed, ct);
        }
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

        await using var ldb = await _dbFactory.CreateDbContextAsync(ct);
        var ids = candidates.Select(f => f.Id).ToList();
        var seen = (await ldb.PlaudImports.Where(r => r.UserId == userId && ids.Contains(r.PlaudFileId))
            .Select(r => r.PlaudFileId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var maxMs = (long)_options.MaxDurationMinutes * 60_000L;
        var added = 0;
        foreach (var f in candidates.Where(f => !seen.Contains(f.Id)))
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

    /// <summary>Imports retryable rows oldest-first. Caps are checked before each get_file.</summary>
    private async Task<(int Imported, string? ErrorCode)> ImportQueuedAsync(PlaudMcpClient.Session session, Guid userId,
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
                .OrderBy(r => r.RecordedAt ?? r.FirstSeenAt)
                .Take(200)
                .ToListAsync(ct);
        }

        var maxPerRun = Math.Max(0, _options.MaxImportsPerRun);
        var maxPerUser = Math.Max(0, _options.MaxInFlightPerUser);
        if (trigger == PlaudSyncTrigger.Backfill)
            maxPerUser = Math.Min(maxPerUser, 1); // spec §8.2: 1 while the user's queue is a backfill
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
            else if (outcome == ImportOutcome.RateLimited)
                return (imported, "rate_limited");
        }
        return (imported, null);
    }

    private enum ImportOutcome { Imported, NotImported, RateLimited }

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
            // Not the recording's fault: put it back, stop the run.
            await SetStatusAsync(row.Id, PlaudImportStatus.Importing, row.Status, null, CancellationToken.None);
            _logger.LogWarning("[PlaudSync] 429 user={UserId} file={FileId}", userId, fileId);
            return ImportOutcome.RateLimited;
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

    private async Task WriteRunStatusAsync(Guid userId, string state, string? errorCode, bool success, int importedCount,
        CancellationToken ct)
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
        await db.SaveChangesAsync(CancellationToken.None);
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
            WindowLocked: sync?.SyncFrom != null);
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
