using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// WI #9131 (spec §3.6, P4) — one ingest path for recordings that arrive as audio files rather than
/// through the vpbot: FIRM Mobile Companion uploads today, Plaud imports next.
/// </summary>
/// <param name="CreatedBy">Owning FIRM user id.</param>
/// <param name="CreatorEntraOid">Owner's Entra object id (keys the personal wiki in the Batch job).</param>
/// <param name="Source">firm_meetings.source, e.g. "mobile" or "plaud". Also names the audio object.</param>
/// <param name="Platform">firm_meetings.platform, e.g. "mobile" or "plaud".</param>
/// <param name="Title">Meeting title (never logged).</param>
/// <param name="RecordedAtUtc">When the recording started, in UTC. Becomes StartDatetime and StartedAt.</param>
/// <param name="DurationSeconds">Recording length if known.</param>
/// <param name="ExternalId">Source-side id (e.g. "plaud:&lt;file_id&gt;"), or null. Unique per user.</param>
/// <param name="FileExtension">Audio file extension including the dot (e.g. ".m4a"); defaults to ".m4a".</param>
public sealed record IngestRequest(
    Guid CreatedBy,
    string CreatorEntraOid,
    string Source,
    string Platform,
    string Title,
    DateTime RecordedAtUtc,
    int? DurationSeconds,
    string? ExternalId,
    string? FileExtension);

/// <summary>Result of <see cref="MeetingIngestService.IngestRecordingAsync"/>. <c>Submitted</c> is false when the
/// Batch submit failed (the caller decides what that means) or when the recording was already imported
/// (<c>AlreadyExisted</c>, in which case <c>MeetingId</c> is the existing meeting and nothing else was done).</summary>
public sealed record IngestResult(long MeetingId, bool Submitted, bool AlreadyExisted = false);

/// <summary>The audio could not be stored. The meeting row (MeetingId) has been marked Failed.</summary>
public sealed class MeetingIngestStorageException : Exception
{
    public long MeetingId { get; }
    public MeetingIngestStorageException(long meetingId, Exception inner)
        : base("Audio upload to storage failed", inner) => MeetingId = meetingId;
}

public class MeetingIngestService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly S3Service _s3Service;
    private readonly MeetingService _meetingService;
    private readonly ILogger<MeetingIngestService> _logger;

    public MeetingIngestService(IDbContextFactory<FirmDbContext> dbFactory, S3Service s3Service,
        MeetingService meetingService, ILogger<MeetingIngestService> logger)
    {
        _dbFactory = dbFactory;
        _s3Service = s3Service;
        _meetingService = meetingService;
        _logger = logger;
    }

    /// <summary>
    /// Inserts the firm_meetings row (StartDatetime = StartedAt = recorded-at), uploads the audio to
    /// firm-audio/{meetingId}/{source}{ext} (multipart-capable), sets AudioS3Key and submits the Batch
    /// transcription job. A duplicate (CreatedBy, ExternalId) returns the existing meeting untouched.
    /// Throws <see cref="MeetingIngestStorageException"/> if the audio upload fails (row marked Failed).
    /// </summary>
    public async Task<IngestResult> IngestRecordingAsync(IngestRequest request, Stream audio, long length, string contentType,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(request.ExternalId))
        {
            var existing = await _meetingService.GetMeetingByExternalIdAsync(request.CreatedBy, request.ExternalId);
            if (existing != null)
            {
                _logger.LogInformation("FIRM: Ingest skipped, {Source} recording already imported as meeting {MeetingId}",
                    request.Source, existing.Id);
                return new IngestResult(existing.Id, Submitted: false, AlreadyExisted: true);
            }
        }

        var recordedAt = request.RecordedAtUtc.Kind == DateTimeKind.Utc
            ? request.RecordedAtUtc
            : request.RecordedAtUtc.ToUniversalTime();
        var now = DateTime.UtcNow;

        // 1. Insert the meeting row.
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var meeting = new FirmMeeting
        {
            Title = request.Title,
            Platform = request.Platform,
            Source = request.Source,
            ExternalId = string.IsNullOrEmpty(request.ExternalId) ? null : request.ExternalId,
            MeetingUrl = null,
            Status = MeetingStatus.Transcribing,
            CreatedBy = request.CreatedBy,
            CreatorEntraOid = request.CreatorEntraOid,
            // The list/detail show and sort by StartDatetime ?? CreatedAt, so both carry the recorded-at time.
            StartDatetime = recordedAt,
            StartedAt = recordedAt,
            EndedAt = request.DurationSeconds.HasValue ? recordedAt.AddSeconds(request.DurationSeconds.Value) : now,
            DurationSeconds = request.DurationSeconds,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Meetings.Add(meeting);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CalendarAutoSyncService.IsDuplicateKeyException(ex)
                                           && !string.IsNullOrEmpty(request.ExternalId))
        {
            // Lost a race with a concurrent import of the same recording: uk_fm_created_by_external_id.
            var existing = await _meetingService.GetMeetingByExternalIdAsync(request.CreatedBy, request.ExternalId);
            if (existing == null) throw;
            _logger.LogInformation("FIRM: Ingest skipped, {Source} recording already imported as meeting {MeetingId} (duplicate key)",
                request.Source, existing.Id);
            return new IngestResult(existing.Id, Submitted: false, AlreadyExisted: true);
        }

        // 2. Upload the audio next to the vpbot's recordings: firm-audio/{meetingId}/{source}{ext}.
        var audioKey = $"firm-audio/{meeting.Id}/{SafeSegment(request.Source)}{NormalizeExtension(request.FileExtension)}";
        try
        {
            await _s3Service.UploadLargeStreamAsync(audioKey, audio,
                string.IsNullOrWhiteSpace(contentType) ? "audio/mp4" : contentType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Ingest S3 write failed for {Source} meeting {MeetingId} ({Bytes} bytes)",
                request.Source, meeting.Id, length);
            meeting.Status = MeetingStatus.Failed;
            meeting.ErrorMessage = "Audio upload to storage failed";
            meeting.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw new MeetingIngestStorageException(meeting.Id, ex);
        }

        // 3. Record the key, then submit the Batch transcription job (same path as vpbot recording_complete).
        meeting.AudioS3Key = audioKey;
        meeting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            var jobId = await _meetingService.SubmitTranscriptionJobAsync(meeting.Id);
            _logger.LogInformation("FIRM: Ingest Batch job {JobId} submitted for {Source} meeting {MeetingId}",
                jobId, request.Source, meeting.Id);
            return new IngestResult(meeting.Id, Submitted: true);
        }
        catch (Exception ex)
        {
            // Audio is stored; the caller decides whether to retry (Plaud) or leave it (mobile, as before).
            _logger.LogError(ex, "FIRM: Ingest failed to submit Batch job for {Source} meeting {MeetingId}",
                request.Source, meeting.Id);
            return new IngestResult(meeting.Id, Submitted: false);
        }
    }

    private static string NormalizeExtension(string? ext)
    {
        var e = (ext ?? "").Trim().ToLowerInvariant();
        if (!e.StartsWith('.')) e = "." + e;
        var body = new string(e.Skip(1).Where(char.IsAsciiLetterOrDigit).ToArray());
        return body.Length is > 0 and <= 10 ? "." + body : ".m4a";
    }

    private static string SafeSegment(string source)
    {
        var s = new string((source ?? "").ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        return s.Length > 0 ? s : "audio";
    }
}
