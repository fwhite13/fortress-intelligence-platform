using System.Text.Json;
using Amazon.S3;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// WI #9139 — chunked browser upload of a recording from any recorder (/upload page).
///
/// The browser sends fixed-size parts to FIRM; FIRM forwards each to an S3 multipart upload at the temporary
/// key <c>firm-uploads/{userId}/{uploadId}/{safeName}</c>. The browser never talks to S3 and never gets AWS
/// credentials or the S3 UploadId.
///
/// State lives in S3 only (several web tasks may serve one upload): <c>firm-uploads/{userId}/{uploadId}/state.json</c>.
/// Every lookup is built from the <b>signed-in</b> user's id, so another user's uploadId simply does not resolve
/// (404). Completion is claimed atomically with a conditional PUT of <c>.../complete.claim</c> (If-None-Match: *),
/// so a retried or doubled "complete" cannot create two meetings. Each part's ETag is recorded at
/// <c>.../parts/{n}.json</c>, so completion needs no S3 ListParts / ListBucket permission: only object-level
/// Get/Put/Delete on the bucket (plus s3:AbortMultipartUpload for an explicit cancel, which is best-effort).
///
/// Abandoned uploads: the page calls the abort endpoint when the user cancels or a part finally fails; anything
/// else (closed tab, lost phone) needs an S3 lifecycle rule on the <c>firm-uploads/</c> prefix
/// (AbortIncompleteMultipartUpload after 1 day, expire objects after 7 days). That rule is infrastructure and is
/// not created by this code.
///
/// Logging: upload id, part numbers, byte counts and status only. Never file names, titles, keys or audio bytes.
/// </summary>
public class RecordingUploadService
{
    public const string Source = "upload";
    public const string Platform = "upload";
    public const string KeyPrefix = "firm-uploads";

    /// <summary>8 MB parts (S3 minimum part size is 5 MB, except the last part).</summary>
    public const long PartSizeBytes = 8L * 1024 * 1024;

    /// <summary>Upper bound for one part request body (part + slack); used by the controller's RequestSizeLimit.</summary>
    public const long MaxPartRequestBytes = PartSizeBytes + 1024 * 1024;

    /// <summary>A completion claim older than this with no meeting recorded is treated as abandoned (task died).</summary>
    private static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(15);

    /// <summary>Extension → canonical content type stored on the S3 object. The allow-list for uploads.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".m4a"] = "audio/mp4",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".aac"] = "audio/aac",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".flac"] = "audio/flac",
        [".caf"] = "audio/x-caf",
        [".mp4"] = "audio/mp4",
        [".m4b"] = "audio/mp4",
    };

    /// <summary>Content types the browser may report. iOS reports an empty type or application/octet-stream for
    /// some audio (.caf, .opus), so those are accepted when the extension is on the allow-list.</summary>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "application/octet-stream",
        "audio/mp4", "audio/x-m4a", "audio/m4a", "audio/mp4a-latm", "audio/x-m4b",
        "audio/mpeg", "audio/mp3", "audio/x-mp3", "audio/mpeg3", "audio/x-mpeg-3",
        "audio/wav", "audio/x-wav", "audio/wave", "audio/vnd.wave",
        "audio/aac", "audio/x-aac", "audio/aacp",
        "audio/ogg", "application/ogg", "audio/opus",
        "audio/flac", "audio/x-flac",
        "audio/x-caf", "audio/caf",
        "video/mp4",
    };

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly S3Service _s3;
    private readonly MeetingIngestService _ingest;
    private readonly IConfiguration _config;
    private readonly ILogger<RecordingUploadService> _logger;

    public RecordingUploadService(S3Service s3, MeetingIngestService ingest, IConfiguration config,
        ILogger<RecordingUploadService> logger)
    {
        _s3 = s3;
        _ingest = ingest;
        _config = config;
        _logger = logger;
    }

    /// <summary>Firm:Upload:MaxAudioBytes, default 500 MB (the same cap as Plaud's MaxAudioBytes).</summary>
    public long MaxAudioBytes => long.TryParse(_config["Firm:Upload:MaxAudioBytes"], out var v) && v > 0 ? v : 500L * 1024 * 1024;

    // ── Validation ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Returns an error message for the client, or null when the file is acceptable.</summary>
    public string? Validate(string? fileName, string? contentType, long size)
    {
        var ext = Path.GetExtension(fileName ?? "");
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.ContainsKey(ext))
            return "This file type is not supported. Choose an audio file (m4a, mp3, wav, aac, ogg, opus, flac, caf, mp4, m4b).";
        var ct = (contentType ?? "").Split(';')[0].Trim();
        if (!AllowedContentTypes.Contains(ct))
            return "This file is not an audio file.";
        if (size <= 0)
            return "The file is empty.";
        if (size > MaxAudioBytes)
            return $"The file is larger than {MaxAudioBytes / (1024 * 1024)} MB.";
        return null;
    }

    public static bool IsValidUploadId(string? uploadId) =>
        Guid.TryParseExact(uploadId, "N", out _);

    public static int PartCountFor(long size) => (int)((size + PartSizeBytes - 1) / PartSizeBytes);

    public static long ExpectedPartSize(UploadState state, int partNumber) =>
        partNumber < state.PartCount ? state.PartSizeBytes : state.SizeBytes - state.PartSizeBytes * (state.PartCount - 1);

    // ── Init ────────────────────────────────────────────────────────────────────────────────────────

    public async Task<UploadState> InitAsync(Guid userId, string fileName, string? contentType, long size,
        string? title, DateTime? recordedAtUtc, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var uploadId = Guid.NewGuid().ToString("N");
        var key = $"{UserPrefix(userId)}{uploadId}/{SafeName(fileName, ext)}";
        var storedType = AllowedExtensions[ext];

        var s3UploadId = await _s3.CreateMultipartUploadAsync(key, storedType, ct);
        var state = new UploadState
        {
            UploadId = uploadId,
            UserId = userId,
            Key = key,
            S3UploadId = s3UploadId,
            Extension = ext,
            ContentType = storedType,
            SizeBytes = size,
            PartSizeBytes = PartSizeBytes,
            PartCount = PartCountFor(size),
            Title = title,
            RecordedAtUtc = recordedAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        };
        await _s3.PutSmallObjectAsync(StateKey(userId, uploadId), JsonSerializer.Serialize(state, JsonOpts), cancellationToken: ct);
        _logger.LogInformation("FIRM: Upload {UploadId} started: {Bytes} bytes in {Parts} parts", uploadId, size, state.PartCount);
        return state;
    }

    // ── State lookup (ownership) ────────────────────────────────────────────────────────────────────

    /// <summary>Loads the upload's state from the <b>caller's</b> prefix. Null for an unknown id or an id owned by
    /// someone else (both resolve to a key that does not exist).</summary>
    public async Task<UploadState?> GetStateAsync(Guid userId, string uploadId, CancellationToken ct)
    {
        if (!IsValidUploadId(uploadId)) return null;
        var obj = await _s3.GetSmallObjectAsync(StateKey(userId, uploadId), ct);
        if (obj == null) return null;
        var state = JsonSerializer.Deserialize<UploadState>(obj.Value.Json, JsonOpts);
        // Belt and braces: the stored owner must match the caller.
        return state != null && state.UserId == userId && state.UploadId == uploadId ? state : null;
    }

    // ── Parts ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Uploads one part, then records its ETag at .../parts/{n}.json. Completion reads these records
    /// instead of S3 ListParts, which needs s3:ListMultipartUploadParts (not in the documented task role).
    /// A retried part overwrites both the S3 part and its record.</summary>
    public async Task UploadPartAsync(UploadState state, int partNumber, Stream content, CancellationToken ct)
    {
        var etag = await _s3.UploadPartAsync(state.Key, state.S3UploadId, partNumber, content, ct);
        await _s3.PutSmallObjectAsync(PartKey(state.UserId, state.UploadId, partNumber),
            JsonSerializer.Serialize(new PartRecord(partNumber, etag, content.Length), JsonOpts), cancellationToken: ct);
        _logger.LogInformation("FIRM: Upload {UploadId} part {Part}/{Parts} stored ({Bytes} bytes)",
            state.UploadId, partNumber, state.PartCount, content.Length);
    }

    // ── Complete ────────────────────────────────────────────────────────────────────────────────────

    public sealed record CompleteOutcome(int StatusCode, string? Error, long? MeetingId, bool Submitted = false);

    /// <summary>
    /// Completes the multipart upload, then (only once the object exists) creates the meeting through
    /// <see cref="MeetingIngestService.IngestStoredObjectAsync"/>. Idempotent: a repeat call after success
    /// returns the same meeting id.
    /// </summary>
    public async Task<CompleteOutcome> CompleteAsync(UploadState state, string creatorEntraOid, string? title,
        DateTime? recordedAtUtc, CancellationToken ct)
    {
        if (state.StorageFailed)
            return new CompleteOutcome(500, "The recording could not be stored. Please upload it again.", null);
        if (state.MeetingId.HasValue)
            return new CompleteOutcome(200, null, state.MeetingId, true);

        // 1. Claim completion atomically, so two "complete" calls cannot both create a meeting.
        var claimKey = ClaimKey(state.UserId, state.UploadId);
        var claimJson = JsonSerializer.Serialize(new { claimedAtUtc = DateTime.UtcNow }, JsonOpts);
        if (!await _s3.PutSmallObjectAsync(claimKey, claimJson, ifNoneMatch: "*", cancellationToken: ct))
        {
            var fresh = await GetStateAsync(state.UserId, state.UploadId, ct);
            if (fresh?.StorageFailed == true)
                return new CompleteOutcome(500, "The recording could not be stored. Please upload it again.", null);
            if (fresh?.MeetingId != null)
                return new CompleteOutcome(200, null, fresh.MeetingId, true);
            var claim = await _s3.GetSmallObjectAsync(claimKey, ct);
            var stale = claim == null || DateTime.UtcNow - claim.Value.LastModifiedUtc > StaleClaimAfter;
            if (!stale || claim == null
                || !await _s3.PutSmallObjectAsync(claimKey, claimJson, ifMatch: claim.Value.ETag, cancellationToken: ct))
            {
                _logger.LogInformation("FIRM: Upload {UploadId} complete already in progress", state.UploadId);
                return new CompleteOutcome(409, "This upload is already being finished. Try again in a minute.", null);
            }
            _logger.LogWarning("FIRM: Upload {UploadId} took over a stale completion claim", state.UploadId);
        }

        var ingestStarted = false;
        try
        {
            // 2. Finish the S3 multipart upload from the part ETags recorded by the part endpoint.
            if (!state.MultipartCompleted)
            {
                var records = await ReadPartRecordsAsync(state, CancellationToken.None);
                var missing = records.Count(r => r == null);
                var received = records.Where(r => r != null).Sum(r => r!.Size);
                if (missing > 0 || received != state.SizeBytes)
                {
                    await ReleaseClaimAsync(claimKey);
                    _logger.LogWarning("FIRM: Upload {UploadId} complete refused: {Missing} parts missing, {Received}/{Expected} bytes",
                        state.UploadId, missing, received, state.SizeBytes);
                    return new CompleteOutcome(400, "Some parts of the file have not arrived yet. Retry the upload.", null);
                }

                try
                {
                    await _s3.CompleteMultipartUploadAsync(state.Key, state.S3UploadId,
                        records.Select(r => (r!.PartNumber, r.ETag)), CancellationToken.None);
                }
                catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchUpload")
                {
                    // Either an earlier attempt completed it (its state save was lost), or it was aborted/expired.
                    // The object check below tells which.
                    _logger.LogWarning("FIRM: Upload {UploadId} multipart no longer open; checking for the assembled object", state.UploadId);
                }
                state.MultipartCompleted = true;
                await SaveStateAsync(state);
            }

            // The assembled object must exist with the declared size before any meeting row is written.
            var size = await _s3.GetObjectSizeAsync(state.Key, CancellationToken.None);
            if (size != state.SizeBytes)
            {
                _logger.LogError("FIRM: Upload {UploadId} assembled object is {Bytes} bytes, expected {Expected}",
                    state.UploadId, size ?? -1, state.SizeBytes);
                return new CompleteOutcome(410, "This upload expired, was cancelled or could not be assembled. Please upload the file again.", null);
            }

            // 3. The object exists: only now insert the meeting row, move the audio and submit the Batch job.
            var finalTitle = !string.IsNullOrWhiteSpace(title) ? title.Trim()
                : !string.IsNullOrWhiteSpace(state.Title) ? state.Title!.Trim()
                : $"Recording {(recordedAtUtc ?? state.RecordedAtUtc ?? DateTime.UtcNow):yyyy-MM-dd HH:mm}";
            if (finalTitle.Length > 500) finalTitle = finalTitle[..500]; // firm_meetings.title is varchar(500)
            var recordedAt = recordedAtUtc ?? state.RecordedAtUtc ?? DateTime.UtcNow;

            var request = new IngestRequest(
                CreatedBy: state.UserId,
                CreatorEntraOid: creatorEntraOid,
                Source: Source,
                Platform: Platform,
                Title: finalTitle,
                RecordedAtUtc: DateTime.SpecifyKind(recordedAt, DateTimeKind.Utc),
                DurationSeconds: null,
                ExternalId: null, // WI #9139: no dedupe for browser uploads.
                FileExtension: state.Extension);

            IngestResult result;
            ingestStarted = true;
            try
            {
                result = await _ingest.IngestStoredObjectAsync(request, state.Key, state.SizeBytes, state.ContentType, CancellationToken.None);
            }
            catch (MeetingIngestStorageException ex)
            {
                // A Failed row exists; record it so a retry does not create a second one.
                state.MeetingId = ex.MeetingId;
                state.StorageFailed = true;
                await SaveStateAsync(state);
                _logger.LogError("FIRM: Upload {UploadId} stored as meeting {MeetingId} but the audio move failed", state.UploadId, ex.MeetingId);
                return new CompleteOutcome(500, "The recording could not be stored. Please upload it again.", null);
            }

            state.MeetingId = result.MeetingId;
            state.CompletedAtUtc = DateTime.UtcNow;
            await SaveStateAsync(state);
            // state.json and the claim stay (so a repeated "complete" returns this meeting); part records go.
            await DeleteBestEffortAsync(state.UploadId,
                Enumerable.Range(1, state.PartCount).Select(n => PartKey(state.UserId, state.UploadId, n)));
            _logger.LogInformation("FIRM: Upload {UploadId} completed as meeting {MeetingId}: {Bytes} bytes, submitted={Submitted}",
                state.UploadId, result.MeetingId, state.SizeBytes, result.Submitted);
            return new CompleteOutcome(200, null, result.MeetingId, result.Submitted);
        }
        catch (Exception ex) // CancellationToken.None after the claim, so a dropped request cannot strand it
        {
            _logger.LogError("FIRM: Upload {UploadId} complete failed (ingest started: {IngestStarted}): {Error}",
                state.UploadId, ingestStarted, ex.GetType().Name);
            if (!ingestStarted)
            {
                // Nothing was created yet, so let a retry claim again.
                await ReleaseClaimAsync(claimKey);
                return new CompleteOutcome(500, "The upload could not be finished. Try again.", null);
            }
            // A meeting row may exist; keep the claim so a retry cannot create a second one.
            return new CompleteOutcome(500, "The upload could not be finished. Check your Meetings list before uploading again.", null);
        }
    }

    // ── Abort ───────────────────────────────────────────────────────────────────────────────────────

    public async Task<bool> AbortAsync(UploadState state, CancellationToken ct)
    {
        if (state.MeetingId.HasValue) return false; // finished uploads are not abortable
        try
        {
            await _s3.AbortMultipartUploadAsync(state.Key, state.S3UploadId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // e.g. the task role lacks s3:AbortMultipartUpload: the lifecycle rule on firm-uploads/ removes the parts.
            _logger.LogWarning("FIRM: Upload {UploadId} S3 abort failed: {Error}", state.UploadId, ex.GetType().Name);
        }
        var keys = new List<string> { state.Key, StateKey(state.UserId, state.UploadId), ClaimKey(state.UserId, state.UploadId) };
        keys.AddRange(Enumerable.Range(1, state.PartCount).Select(n => PartKey(state.UserId, state.UploadId, n)));
        await DeleteBestEffortAsync(state.UploadId, keys);
        _logger.LogInformation("FIRM: Upload {UploadId} aborted", state.UploadId);
        return true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private sealed record PartRecord(int PartNumber, string ETag, long Size);

    /// <summary>One entry per expected part (1..PartCount), null where no valid record exists.</summary>
    private async Task<List<PartRecord?>> ReadPartRecordsAsync(UploadState state, CancellationToken ct)
    {
        var result = new PartRecord?[state.PartCount];
        using var gate = new SemaphoreSlim(8);
        await Task.WhenAll(Enumerable.Range(1, state.PartCount).Select(async n =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var obj = await _s3.GetSmallObjectAsync(PartKey(state.UserId, state.UploadId, n), ct);
                if (obj == null) return;
                var rec = JsonSerializer.Deserialize<PartRecord>(obj.Value.Json, JsonOpts);
                if (rec != null && rec.PartNumber == n && !string.IsNullOrEmpty(rec.ETag)
                    && rec.Size == ExpectedPartSize(state, n))
                    result[n - 1] = rec;
            }
            finally
            {
                gate.Release();
            }
        }));
        return result.ToList();
    }

    private async Task DeleteBestEffortAsync(string uploadId, IEnumerable<string> keys)
    {
        var failures = 0;
        using var gate = new SemaphoreSlim(8);
        await Task.WhenAll(keys.Select(async k =>
        {
            await gate.WaitAsync();
            try { await _s3.DeleteObjectAsync(k, CancellationToken.None); }
            catch (Exception) { Interlocked.Increment(ref failures); }
            finally { gate.Release(); }
        }));
        if (failures > 0)
            _logger.LogWarning("FIRM: Upload {UploadId} cleanup left {Count} objects for the lifecycle rule", uploadId, failures);
    }

    private async Task SaveStateAsync(UploadState state)
    {
        try
        {
            await _s3.PutSmallObjectAsync(StateKey(state.UserId, state.UploadId), JsonSerializer.Serialize(state, JsonOpts),
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("FIRM: Upload {UploadId} state save failed: {Error}", state.UploadId, ex.GetType().Name);
        }
    }

    private async Task ReleaseClaimAsync(string claimKey)
    {
        try { await _s3.DeleteObjectAsync(claimKey, CancellationToken.None); }
        catch (Exception ex) { _logger.LogWarning("FIRM: Upload claim release failed: {Error}", ex.GetType().Name); }
    }

    private static string UserPrefix(Guid userId) => $"{KeyPrefix}/{userId:D}/";
    private static string StateKey(Guid userId, string uploadId) => $"{UserPrefix(userId)}{uploadId}/state.json";
    private static string ClaimKey(Guid userId, string uploadId) => $"{UserPrefix(userId)}{uploadId}/complete.claim";
    private static string PartKey(Guid userId, string uploadId, int partNumber) => $"{UserPrefix(userId)}{uploadId}/parts/{partNumber:D5}.json";

    /// <summary>File name reduced to [a-z0-9._-], at most 80 chars, with the validated extension.</summary>
    private static string SafeName(string fileName, string ext)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var safe = new string(stem.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        if (safe.Length > 80) safe = safe[..80];
        if (safe.Length == 0 || safe is "state" or "complete") safe = "recording";
        return safe + ext;
    }
}

/// <summary>WI #9139 — persisted at firm-uploads/{userId}/{uploadId}/state.json. Never returned to the browser.</summary>
public sealed class UploadState
{
    public string UploadId { get; set; } = "";
    public Guid UserId { get; set; }
    public string Key { get; set; } = "";
    public string S3UploadId { get; set; } = "";
    public string Extension { get; set; } = ".m4a";
    public string ContentType { get; set; } = "audio/mp4";
    public long SizeBytes { get; set; }
    public long PartSizeBytes { get; set; }
    public int PartCount { get; set; }
    public string? Title { get; set; }
    public DateTime? RecordedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public long? MeetingId { get; set; }
    public bool StorageFailed { get; set; }
    public bool MultipartCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
