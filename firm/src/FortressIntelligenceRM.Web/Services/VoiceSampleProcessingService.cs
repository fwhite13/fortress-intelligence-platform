using Amazon.Batch;
using Amazon.Batch.Model;
using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
using FortressIntelligenceRM.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

// WI #8298 — voice enrollment: stores the user's recorded sample in S3 and submits a Batch job
// (JOB_MODE=voice_sample) that extracts the embedding and writes the voice_sample row.
public class VoiceSampleProcessingService
{
    public const long MaxSampleBytes = 10 * 1024 * 1024;

    // WI #8828 — meeting-based enrollment. Windows are counted the way transcribe.py's
    // extract_reference_embeddings picks them, so a candidate here is one the job can actually use.
    public const double MinCandidateSeconds = 20.0;
    private const double PreviewMaxSeconds = 15.0;
    private const double ReferenceMinWindowSeconds = 2.0;  // REFERENCE_MIN_WINDOW_S
    private const long ReferenceMergeGapMs = 1000;         // REFERENCE_MERGE_GAP_MS
    private const int CandidateMeetingScanLimit = 200;
    private static readonly TimeSpan PreviewUrlExpiry = TimeSpan.FromMinutes(15);

    private readonly IAmazonBatch _batch;
    private readonly IAmazonS3 _s3;
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly BrandingConfig _branding;
    private readonly ILogger<VoiceSampleProcessingService> _logger;
    private string JobQueue => _config["Firm:BatchJobQueue"] ?? "firm-transcription-queue";
    private string JobDefinition => _config["Firm:BatchJobDefinition"] ?? "firm-transcription-job";
    private string S3Bucket => _config["Firm:S3Bucket"] ?? "firm-recordings-dev";

    public VoiceSampleProcessingService(IAmazonBatch batch, IAmazonS3 s3, IDbContextFactory<FirmDbContext> dbFactory, IConfiguration config, BrandingConfig branding, ILogger<VoiceSampleProcessingService> logger)
    {
        _batch = batch;
        _s3 = s3;
        _dbFactory = dbFactory;
        _config = config;
        _branding = branding;
        _logger = logger;
    }

    // Returns the Batch job id. Extraction runs in the background — the enrollment status updates once the job writes the row.
    public async Task<string> SubmitAsync(string entraOid, string displayName, byte[] audio, CancellationToken ct = default)
    {
        if (audio.Length == 0)
            throw new ArgumentException("Voice sample is empty", nameof(audio));
        if (audio.Length > MaxSampleBytes)
            throw new ArgumentException("Voice sample is too large", nameof(audio));

        var tenantId = _config["Firm:GraphTenantId"] ?? "";
        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("Firm:GraphTenantId is not configured");

        var s3Key = $"voice-samples/{entraOid}/{Guid.NewGuid()}.webm";
        using (var body = new MemoryStream(audio))
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = S3Bucket,
                Key = s3Key,
                InputStream = body,
                ContentType = "audio/webm"
            }, ct);
        }
        _logger.LogInformation("FIRM: Voice sample for {EntraOid} uploaded to s3://{Bucket}/{Key} ({Bytes} bytes)", entraOid, S3Bucket, s3Key, audio.Length);

        var envVars = new List<Amazon.Batch.Model.KeyValuePair>
        {
            new Amazon.Batch.Model.KeyValuePair { Name = "JOB_MODE", Value = "voice_sample" },
            new Amazon.Batch.Model.KeyValuePair { Name = "VOICE_SAMPLE_S3_KEY", Value = s3Key },
        };
        return await SubmitVoiceprintJobAsync("voice-sample", entraOid, displayName, tenantId, envVars, ct);
    }

    // WI #8828 — meetings with enough clean active-speaker audio attributed to this user, best first.
    // Speaking in the meeting's active speaker log is what makes the user an attendee here.
    public async Task<List<EnrollmentCandidate>> GetEnrollmentCandidatesAsync(string displayName, CancellationToken ct = default)
    {
        var name = displayName.Trim();
        if (name == "")
            return new List<EnrollmentCandidate>();

        var meetings = await LoadMeetingsWithSpeechAsync(name, null, ct);
        return meetings
            .Select(m => (Meeting: m, Windows: GetCleanWindows(m, name)))
            .Where(x => x.Windows != null && x.Windows.Sum(w => w.EndSec - w.StartSec) >= MinCandidateSeconds)
            .Select(x => new EnrollmentCandidate(
                x.Meeting.Id,
                string.IsNullOrWhiteSpace(x.Meeting.Title) ? "Untitled meeting" : x.Meeting.Title!,
                DateOnly.FromDateTime(x.Meeting.StartedAt ?? x.Meeting.StartDatetime ?? x.Meeting.CreatedAt),
                Math.Round(x.Windows!.Sum(w => w.EndSec - w.StartSec), 1),
                x.Windows!.Count,
                x.Meeting.AudioS3Key!,
                ActiveSpeakerS3Key(x.Meeting.Id)))
            .OrderByDescending(c => c.AttributedSeconds)
            .ToList();
    }

    // Presigned recording URL plus the longest clean window (capped at 15s) for the listen-and-confirm preview.
    public async Task<EnrollmentClip?> GetEnrollmentClipAsync(string displayName, long meetingId, CancellationToken ct = default)
    {
        var meeting = await GetCandidateMeetingAsync(displayName, meetingId, ct);
        if (meeting == null)
            return null;

        var longest = meeting.Value.Windows.MaxBy(w => w.EndSec - w.StartSec);
        var url = await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = S3Bucket,
            Key = meeting.Value.Meeting.AudioS3Key,
            Expires = DateTime.UtcNow.Add(PreviewUrlExpiry)
        });
        var start = Math.Round(longest.StartSec, 1);
        var end = Math.Round(Math.Min(longest.EndSec, longest.StartSec + PreviewMaxSeconds), 1);
        return new EnrollmentClip(url, start, end);
    }

    // WI #8828 — channel-matched enrollment: the transcriber (JOB_MODE=voice_sample_from_meeting) embeds this
    // user's clean active-speaker windows from the meeting recording. Returns null if the meeting isn't a candidate.
    public async Task<string?> SubmitFromMeetingAsync(string entraOid, string displayName, long meetingId, CancellationToken ct = default)
    {
        var tenantId = _config["Firm:GraphTenantId"] ?? "";
        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("Firm:GraphTenantId is not configured");

        var candidate = await GetCandidateMeetingAsync(displayName, meetingId, ct);
        if (candidate == null)
            return null;
        var meeting = candidate.Value.Meeting;

        // Re-stage the log from the DB so the job never depends on an earlier transcription having uploaded it.
        var activeSpeakerKey = ActiveSpeakerS3Key(meeting.Id);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = S3Bucket,
            Key = activeSpeakerKey,
            ContentBody = meeting.ActiveSpeakerTimeline,
            ContentType = "application/json"
        }, ct);

        var envVars = new List<Amazon.Batch.Model.KeyValuePair>
        {
            new Amazon.Batch.Model.KeyValuePair { Name = "JOB_MODE", Value = "voice_sample_from_meeting" },
            new Amazon.Batch.Model.KeyValuePair { Name = "MEETING_ID", Value = meeting.Id.ToString() },
            new Amazon.Batch.Model.KeyValuePair { Name = "MEETING_AUDIO_S3_KEY", Value = meeting.AudioS3Key! },
            new Amazon.Batch.Model.KeyValuePair { Name = "ACTIVE_SPEAKER_S3_KEY", Value = activeSpeakerKey },
            new Amazon.Batch.Model.KeyValuePair { Name = "NOTETAKER_NAME", Value = _branding.NotetakerName },
        };
        // The same recording-start base the preview offsets use, so the job embeds exactly the windows the user heard.
        if (candidate.Value.RecordingStartMs is { } recordingStartMs)
            envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "RECORDING_START_MS", Value = recordingStartMs.ToString() });

        _logger.LogInformation("FIRM: Voice enrollment from meeting {MeetingId} for {EntraOid} ({Windows} window(s))",
            meeting.Id, entraOid, candidate.Value.Windows.Count);
        return await SubmitVoiceprintJobAsync("voice-sample-meeting", entraOid, displayName.Trim(), tenantId, envVars, ct);
    }

    private static string ActiveSpeakerS3Key(long meetingId) => $"firm-audio/{meetingId}/active_speaker.json";

    private async Task<(Models.FirmMeeting Meeting, List<SpeechWindow> Windows, long? RecordingStartMs)?> GetCandidateMeetingAsync(
        string displayName, long meetingId, CancellationToken ct)
    {
        var name = displayName.Trim();
        if (name == "")
            return null;
        var meeting = (await LoadMeetingsWithSpeechAsync(name, meetingId, ct)).FirstOrDefault();
        if (meeting == null)
            return null;
        var windows = GetCleanWindows(meeting, name, out var recordingStartMs);
        if (windows == null || windows.Sum(w => w.EndSec - w.StartSec) < MinCandidateSeconds)
            return null;
        return (meeting, windows, recordingStartMs);
    }

    // Primary recordings with audio and an active speaker log that may mention the user. Subscriber rows never
    // carry the log, so each real-world meeting appears once.
    private async Task<List<Models.FirmMeeting>> LoadMeetingsWithSpeechAsync(string name, long? meetingId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.Meetings.AsNoTracking()
            .Where(m => m.IsPrimaryRecorder && m.AudioS3Key != null && m.AudioS3Key != "" && m.ActiveSpeakerTimeline != null);
        if (meetingId is { } id)
            query = query.Where(m => m.Id == id);
        // The log is stored by System.Text.Json, which escapes non-ASCII and quote characters — only pre-filter
        // in SQL when the name would be stored verbatim; GetCleanWindows does the exact match either way.
        if (name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '.' or '-' or ','))
            query = query.Where(m => m.ActiveSpeakerTimeline!.Contains(name));
        return await query
            .OrderByDescending(m => m.Id)
            .Take(CandidateMeetingScanLimit)
            .Select(m => new Models.FirmMeeting
            {
                Id = m.Id,
                Title = m.Title,
                StartedAt = m.StartedAt,
                StartDatetime = m.StartDatetime,
                CreatedAt = m.CreatedAt,
                AudioS3Key = m.AudioS3Key,
                RosterTimeline = m.RosterTimeline,
                ActiveSpeakerTimeline = m.ActiveSpeakerTimeline
            })
            .ToListAsync(ct);
    }

    private List<SpeechWindow>? GetCleanWindows(Models.FirmMeeting meeting, string name) => GetCleanWindows(meeting, name, out _);

    // Mirrors transcribe.py: drop the notetaker, merge each person's entries closer than 1s, rebase epoch-ms logs on
    // the roster's earliest join (the approximate recording start), and keep this user's windows that are >= 2s and
    // don't overlap anyone else's. Null when the user's audio is unusable (no entries, or possiblyMultiVoice).
    private List<SpeechWindow>? GetCleanWindows(Models.FirmMeeting meeting, string name, out long? recordingStartMs)
    {
        recordingStartMs = null;
        List<LogEntry> log;
        try
        {
            log = (JsonSerializer.Deserialize<List<JsonElement>>(meeting.ActiveSpeakerTimeline!) ?? new())
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => new LogEntry(
                    e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "",
                    e.TryGetProperty("startMs", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : null,
                    e.TryGetProperty("endMs", out var en) && en.ValueKind == JsonValueKind.Number ? en.GetInt64() : null,
                    e.TryGetProperty("possiblyMultiVoice", out var mv) && mv.ValueKind == JsonValueKind.True))
                .Where(e => e.Name != "" && e.StartMs != null && e.Name != _branding.NotetakerName)
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(ex, "FIRM: Failed to parse active_speaker_timeline for meeting {MeetingId}", meeting.Id);
            return null;
        }
        if (log.Count == 0)
            return null;

        var logName = log.Select(e => e.Name).FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (logName == null || IsMultiVoice(meeting, log, logName))
            return null;

        var minStart = log.Min(e => e.StartMs!.Value);
        long baseMs = 0;
        if (minStart > 1_000_000_000_000)
        {
            baseMs = RosterEarliestJoinMs(meeting) ?? minStart;
            recordingStartMs = baseMs;
        }

        var merged = log
            .OrderBy(e => e.StartMs)
            .GroupBy(e => e.Name)
            .ToDictionary(g => g.Key, g =>
            {
                var wins = new List<(long Start, long End)>();
                foreach (var e in g)
                {
                    var end = e.EndMs ?? e.StartMs!.Value;
                    if (wins.Count > 0 && e.StartMs!.Value - wins[^1].End <= ReferenceMergeGapMs)
                        wins[^1] = (wins[^1].Start, Math.Max(wins[^1].End, end));
                    else
                        wins.Add((e.StartMs!.Value, end));
                }
                return wins;
            });
        var others = merged.Where(kv => kv.Key != logName).SelectMany(kv => kv.Value).ToList();

        return merged[logName]
            .Where(w => (w.End - w.Start) / 1000.0 >= ReferenceMinWindowSeconds)
            .Where(w => !others.Any(o => o.Start < w.End && o.End > w.Start))
            .Select(w => new SpeechWindow(Math.Max(0, (w.Start - baseMs) / 1000.0), (w.End - baseMs) / 1000.0))
            .ToList();
    }

    // extract_reference_embeddings skips anyone flagged possiblyMultiVoice in the log or the roster (shared room device).
    private static bool IsMultiVoice(Models.FirmMeeting meeting, List<LogEntry> log, string logName)
    {
        if (log.Any(e => e.Name == logName && e.PossiblyMultiVoice))
            return true;
        return ParseRoster(meeting).Any(r => r.Name == logName && r.PossiblyMultiVoice);
    }

    private long? RosterEarliestJoinMs(Models.FirmMeeting meeting)
    {
        var joins = ParseRoster(meeting).Where(r => r.Name != _branding.NotetakerName && r.JoinedAtMs != null).Select(r => r.JoinedAtMs!.Value).ToList();
        return joins.Count > 0 ? joins.Min() : null;
    }

    private static List<(string Name, long? JoinedAtMs, bool PossiblyMultiVoice)> ParseRoster(Models.FirmMeeting meeting)
    {
        if (string.IsNullOrWhiteSpace(meeting.RosterTimeline))
            return new();
        try
        {
            return (JsonSerializer.Deserialize<List<JsonElement>>(meeting.RosterTimeline) ?? new())
                .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                .Select(e => (
                    e.GetProperty("name").GetString()!.Trim(),
                    e.TryGetProperty("joinedAtMs", out var j) && j.ValueKind == JsonValueKind.Number ? j.GetInt64() : (long?)null,
                    e.TryGetProperty("possiblyMultiVoice", out var mv) && mv.ValueKind == JsonValueKind.True))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return new();
        }
    }

    private record LogEntry(string Name, long? StartMs, long? EndMs, bool PossiblyMultiVoice);
    private readonly record struct SpeechWindow(double StartSec, double EndSec);

    // Shared by both enrollment paths: adds the identity/DB/HF env vars, submits the Batch job and marks the user pending.
    private async Task<string> SubmitVoiceprintJobAsync(string jobNamePrefix, string entraOid, string displayName, string tenantId,
        List<Amazon.Batch.Model.KeyValuePair> envVars, CancellationToken ct)
    {
        envVars.AddRange(new[]
        {
            new Amazon.Batch.Model.KeyValuePair { Name = "ENTRA_OID", Value = entraOid },
            new Amazon.Batch.Model.KeyValuePair { Name = "TENANT_ID", Value = tenantId },
            new Amazon.Batch.Model.KeyValuePair { Name = "FIRM_SPEAKER_EMBEDDING_NAME", Value = displayName },
            new Amazon.Batch.Model.KeyValuePair { Name = "S3_BUCKET", Value = S3Bucket },
            new Amazon.Batch.Model.KeyValuePair { Name = "PYANNOTE_CACHE", Value = "/app/.cache/huggingface/hub" },
            new Amazon.Batch.Model.KeyValuePair { Name = "FORTRESS_DB_HOST", Value = _config["FORTRESS_DB_HOST"] ?? "" },
            new Amazon.Batch.Model.KeyValuePair { Name = "FORTRESS_DB_PORT", Value = _config["FORTRESS_DB_PORT"] ?? "3306" },
            new Amazon.Batch.Model.KeyValuePair { Name = "FORTRESS_DB_USER", Value = _config["FORTRESS_DB_USER"] ?? "fortress_mysql" },
            new Amazon.Batch.Model.KeyValuePair { Name = "FIRM_DB_NAME", Value = _config["FIRM_DB_NAME"] ?? "firm_dev" },
        });
        // WI #8305 — the voice_sample job needs the HF token (pyannote embedding model) and the DB password
        // (firm_speaker_embeddings write). Only override when configured, so an empty value never clobbers
        // one supplied by the job definition's Secrets Manager mapping.
        var hfToken = _config["HuggingFace:Token"] ?? _config["HF_TOKEN"];
        if (!string.IsNullOrEmpty(hfToken))
            envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "HF_TOKEN", Value = hfToken });
        else
            _logger.LogWarning("FIRM: HF_TOKEN is not configured — voice sample job relies on the job definition for it");

        var dbPass = _config["FORTRESS_DB_PASS"];
        if (!string.IsNullOrEmpty(dbPass))
            envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "FORTRESS_DB_PASS", Value = dbPass });
        else
            _logger.LogWarning("FIRM: FORTRESS_DB_PASS is not configured — voice sample job relies on the job definition for it");

        var response = await _batch.SubmitJobAsync(new SubmitJobRequest
        {
            JobName = $"{jobNamePrefix}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            JobQueue = JobQueue,
            JobDefinition = JobDefinition,
            ContainerOverrides = new ContainerOverrides { Environment = envVars }
        }, ct);
        _logger.LogInformation("FIRM: Voiceprint Batch job {JobId} ({Prefix}) submitted for {EntraOid}", response.JobId, jobNamePrefix, entraOid);

        // WI #8349 — mark the user pending so the panel shows progress and blocks a re-submit. Cleared by
        // SpeakerEmbeddingService.GetEnrollmentStatusAsync once the job's voice_sample row appears.
        // The job is already running, so a failure here must not surface as a failed submission.
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
            var pendingSince = DateTime.UtcNow;
            var updated = await db.Users
                .Where(u => u.EntraOid == entraOid)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.VoiceEnrollmentPendingSince, pendingSince), CancellationToken.None);
            if (updated == 0)
                _logger.LogWarning("FIRM: No firm_users row for {EntraOid} — voice enrollment pending state not recorded", entraOid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to record voice enrollment pending state for {EntraOid}", entraOid);
        }

        return response.JobId;
    }
}

public record EnrollmentCandidate(long MeetingId, string Title, DateOnly Date, double AttributedSeconds, int WindowCount,
    string AudioS3Key, string ActiveSpeakerS3Key);

public record EnrollmentClip(string PresignedUrl, double PreviewStartSec, double PreviewEndSec);
