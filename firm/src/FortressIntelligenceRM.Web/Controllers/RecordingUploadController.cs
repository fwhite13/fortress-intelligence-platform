using FortressIntelligenceRM.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FortressIntelligenceRM.Web.Controllers;

/// <summary>
/// WI #9139 — chunked recording upload for the /upload page (any recorder; iPhone Safari first).
///
///   POST   /api/uploads/recording/init                     → { uploadId, partSize, partCount, maxBytes }
///   PUT    /api/uploads/recording/{uploadId}/part/{n}      → raw bytes of part n (1-based, 8 MB except the last)
///   POST   /api/uploads/recording/{uploadId}/complete      → { meetingId, status }
///   DELETE /api/uploads/recording/{uploadId}               → abort (cancel / final failure)
///
/// Every action is [Authorize] with the same "CookieOrBearer" policy and the same user resolution as
/// MeetingsApiController (oid claim → MeetingService.GetOrCreateUserAsync). An uploadId is only ever looked up
/// under the signed-in user's own S3 prefix, so another user's id returns 404. Responses never contain AWS
/// credentials, S3 keys or the S3 multipart UploadId. Logs carry upload id, part numbers, byte counts and status
/// only (see RecordingUploadService).
/// </summary>
[ApiController]
[Route("api/uploads/recording")]
[Authorize(Policy = "CookieOrBearer")]
public class RecordingUploadController : ControllerBase
{
    private readonly RecordingUploadService _uploads;
    private readonly MeetingService _meetingService;
    private readonly ILogger<RecordingUploadController> _logger;

    public RecordingUploadController(RecordingUploadService uploads, MeetingService meetingService,
        ILogger<RecordingUploadController> logger)
    {
        _uploads = uploads;
        _meetingService = meetingService;
        _logger = logger;
    }

    public sealed class InitRequest
    {
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public long Size { get; set; }
        public string? Title { get; set; }
        public DateTimeOffset? RecordedAt { get; set; }
    }

    public sealed class CompleteRequest
    {
        public string? Title { get; set; }
        public DateTimeOffset? RecordedAt { get; set; }
    }

    [HttpPost("init")]
    [Authorize(Policy = "CookieOrBearer")]
    public async Task<IActionResult> Init([FromBody] InitRequest request)
    {
        var user = await ResolveUserAsync();
        if (user == null) return Unauthorized();

        var error = _uploads.Validate(request.FileName, request.ContentType, request.Size);
        if (error != null)
        {
            _logger.LogInformation("FIRM: Upload init refused ({Bytes} bytes): validation failed", request.Size);
            return BadRequest(new { error });
        }

        var state = await _uploads.InitAsync(user.Value.UserId, request.FileName!, request.ContentType, request.Size,
            string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
            request.RecordedAt?.UtcDateTime, HttpContext.RequestAborted);

        return Ok(new
        {
            uploadId = state.UploadId,
            partSize = state.PartSizeBytes,
            partCount = state.PartCount,
            maxBytes = _uploads.MaxAudioBytes
        });
    }

    /// <summary>One part as the raw request body (application/octet-stream). The request size limit is raised
    /// only here, and only to one part plus slack; Kestrel's global default stays as it is.</summary>
    [HttpPut("{uploadId}/part/{partNumber:int}")]
    [Authorize(Policy = "CookieOrBearer")]
    [RequestSizeLimit(RecordingUploadService.MaxPartRequestBytes)]
    public async Task<IActionResult> UploadPart(string uploadId, int partNumber)
    {
        var user = await ResolveUserAsync();
        if (user == null) return Unauthorized();

        var state = await _uploads.GetStateAsync(user.Value.UserId, uploadId, HttpContext.RequestAborted);
        if (state == null) return NotFound(new { error = "Upload not found" });
        if (state.MeetingId.HasValue) return Conflict(new { error = "This upload is already finished" });
        if (partNumber < 1 || partNumber > state.PartCount)
            return BadRequest(new { error = "Part number out of range" });

        var expected = RecordingUploadService.ExpectedPartSize(state, partNumber);
        if (Request.ContentLength.HasValue && Request.ContentLength.Value != expected)
            return BadRequest(new { error = "Part has the wrong size" });

        // Buffer the part (≤ 8 MB) so the S3 SDK gets a seekable stream with a known length.
        using var buffer = new MemoryStream((int)expected);
        await Request.Body.CopyToAsync(buffer, HttpContext.RequestAborted);
        if (buffer.Length != expected)
        {
            _logger.LogInformation("FIRM: Upload {UploadId} part {Part} refused: {Bytes} bytes, expected {Expected}",
                state.UploadId, partNumber, buffer.Length, expected);
            return BadRequest(new { error = "Part has the wrong size" });
        }
        buffer.Position = 0;

        try
        {
            await _uploads.UploadPartAsync(state, partNumber, buffer, HttpContext.RequestAborted);
        }
        catch (Amazon.S3.AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchUpload")
        {
            return StatusCode(410, new { error = "This upload expired or was cancelled. Please upload the file again." });
        }
        return Ok(new { partNumber, size = expected });
    }

    [HttpPost("{uploadId}/complete")]
    [Authorize(Policy = "CookieOrBearer")]
    public async Task<IActionResult> Complete(string uploadId, [FromBody] CompleteRequest? request)
    {
        var user = await ResolveUserAsync();
        if (user == null) return Unauthorized();

        var state = await _uploads.GetStateAsync(user.Value.UserId, uploadId, HttpContext.RequestAborted);
        if (state == null) return NotFound(new { error = "Upload not found" });

        var outcome = await _uploads.CompleteAsync(state, user.Value.EntraOid, request?.Title,
            request?.RecordedAt?.UtcDateTime, HttpContext.RequestAborted);
        if (outcome.StatusCode != 200)
            return StatusCode(outcome.StatusCode, new { error = outcome.Error });

        return Ok(new
        {
            meetingId = outcome.MeetingId,
            status = "transcribing",
            url = $"/meetings/{outcome.MeetingId}"
        });
    }

    [HttpDelete("{uploadId}")]
    [Authorize(Policy = "CookieOrBearer")]
    public async Task<IActionResult> Abort(string uploadId)
    {
        var user = await ResolveUserAsync();
        if (user == null) return Unauthorized();

        var state = await _uploads.GetStateAsync(user.Value.UserId, uploadId, HttpContext.RequestAborted);
        if (state == null) return NotFound(new { error = "Upload not found" });

        return await _uploads.AbortAsync(state, HttpContext.RequestAborted)
            ? NoContent()
            : Conflict(new { error = "This upload is already finished" });
    }

    /// <summary>Same user resolution as MeetingsApiController.MobileUpload.</summary>
    private async Task<(Guid UserId, string EntraOid)?> ResolveUserAsync()
    {
        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(entraOid)) return null;
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? User.FindFirst("preferred_username")?.Value ?? "";
        var displayName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? User.FindFirst("name")?.Value ?? email;
        var firmUser = await _meetingService.GetOrCreateUserAsync(entraOid, email, displayName);
        return firmUser == null ? null : (firmUser.Id, entraOid);
    }
}
