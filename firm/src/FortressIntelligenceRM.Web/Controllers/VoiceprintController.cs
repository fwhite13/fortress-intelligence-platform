using FortressIntelligenceRM.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FortressIntelligenceRM.Web.Controllers;

// WI #8298 — self-service voice enrollment. The settings UI calls the services directly over the
// Blazor circuit; these endpoints expose the same flow to API clients.
[ApiController]
[Route("api/voiceprint")]
[Authorize]
public class VoiceprintController : ControllerBase
{
    private readonly VoiceSampleProcessingService _voiceSamples;
    private readonly SpeakerEmbeddingService _embeddings;
    private readonly ILogger<VoiceprintController> _logger;

    public VoiceprintController(VoiceSampleProcessingService voiceSamples, SpeakerEmbeddingService embeddings, ILogger<VoiceprintController> logger)
    {
        _voiceSamples = voiceSamples;
        _embeddings = embeddings;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var entraOid = GetEntraOid();
        if (string.IsNullOrEmpty(entraOid)) return BadRequest(new { error = "User identity not available" });

        var status = await _embeddings.GetEnrollmentStatusAsync(entraOid, GetDisplayName());
        return Ok(new
        {
            enrolled = status.IsEnrolled,
            pending = status.IsPending,
            recordedAt = status.VoiceSampleRecordedAt,
            autoMeetingCount = status.AutoMeetingCount
        });
    }

    // Body: the raw recorded audio (audio/webm). Returns immediately; extraction runs in Batch.
    [HttpPost("upload")]
    [RequestSizeLimit(VoiceSampleProcessingService.MaxSampleBytes)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        var entraOid = GetEntraOid();
        if (string.IsNullOrEmpty(entraOid)) return BadRequest(new { error = "User identity not available" });

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        if (buffer.Length == 0) return BadRequest(new { error = "Empty voice sample" });

        try
        {
            var jobId = await _voiceSamples.SubmitAsync(entraOid, GetDisplayName(), buffer.ToArray(), ct);
            return Accepted(new { jobId });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // WI #8828 — meetings this user spoke in with enough clean attributed audio to enroll from, best first.
    [HttpGet("enrollment-candidates")]
    public async Task<IActionResult> EnrollmentCandidates(CancellationToken ct)
    {
        var entraOid = GetEntraOid();
        if (string.IsNullOrEmpty(entraOid)) return BadRequest(new { error = "User identity not available" });

        var candidates = await _voiceSamples.GetEnrollmentCandidatesAsync(GetDisplayName(), ct);
        return Ok(candidates.Select(c => new
        {
            meetingId = c.MeetingId,
            title = c.Title,
            date = c.Date.ToString("yyyy-MM-dd"),
            attributedSeconds = c.AttributedSeconds,
            windowCount = c.WindowCount,
            audioS3Key = c.AudioS3Key,
            activeSpeakerS3Key = c.ActiveSpeakerS3Key
        }));
    }

    // WI #8828 — presigned recording URL plus the preview segment (the user's longest clean window, up to 15s).
    [HttpGet("enrollment-clip")]
    public async Task<IActionResult> EnrollmentClip([FromQuery] long meetingId, CancellationToken ct)
    {
        var entraOid = GetEntraOid();
        if (string.IsNullOrEmpty(entraOid)) return BadRequest(new { error = "User identity not available" });

        var clip = await _voiceSamples.GetEnrollmentClipAsync(GetDisplayName(), meetingId, ct);
        if (clip == null) return NotFound(new { error = "Meeting is not an enrollment candidate for this user" });
        return Ok(new { presignedUrl = clip.PresignedUrl, previewStartSec = clip.PreviewStartSec, previewEndSec = clip.PreviewEndSec });
    }

    // WI #8828 — channel-matched enrollment from a meeting recording. Returns immediately; extraction runs in Batch.
    [HttpPost("enroll-from-meeting")]
    public async Task<IActionResult> EnrollFromMeeting([FromBody] EnrollFromMeetingRequest request, CancellationToken ct)
    {
        var entraOid = GetEntraOid();
        if (string.IsNullOrEmpty(entraOid)) return BadRequest(new { error = "User identity not available" });

        var jobId = await _voiceSamples.SubmitFromMeetingAsync(entraOid, GetDisplayName(), request.MeetingId, ct);
        if (jobId == null) return NotFound(new { error = "Meeting is not an enrollment candidate for this user" });
        return Accepted(new { jobId });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete()
    {
        var entraOid = GetEntraOid();
        if (string.IsNullOrEmpty(entraOid)) return BadRequest(new { error = "User identity not available" });

        var count = await _embeddings.DeactivateVoiceSamplesAsync(entraOid);
        return Ok(new { deactivated = count });
    }

    private string? GetEntraOid() =>
        User.FindFirst("oid")?.Value
        ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

    private string GetDisplayName() => User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "";
}

public record EnrollFromMeetingRequest(long MeetingId);
