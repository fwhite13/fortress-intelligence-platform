using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FortressAI.Web.Services;
using System.Security.Claims;

namespace FortressAI.Web.Controllers;

[ApiController]
[Route("api/chat-attachments")]
[Authorize]
public class ChatAttachmentController : ControllerBase
{
    private readonly ChatAttachmentService _attachmentService;
    private readonly ILogger<ChatAttachmentController> _logger;
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

    public ChatAttachmentController(
        ChatAttachmentService attachmentService,
        ILogger<ChatAttachmentController> logger)
    {
        _attachmentService = attachmentService;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromQuery] Guid conversationId,
        IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided" });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { error = "File exceeds 10MB limit" });

        if (!ChatAttachmentService.IsSupportedFile(file.FileName))
            return BadRequest(new { error = $"File type not supported: {Path.GetExtension(file.FileName)}" });

        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        try
        {
            using var stream = file.OpenReadStream();
            var attachment = await _attachmentService.UploadAttachmentAsync(
                conversationId,
                userId,
                file.FileName,
                file.ContentType ?? "application/octet-stream",
                stream,
                file.Length);

            return Ok(new
            {
                id = attachment.Id,
                filename = attachment.Filename,
                contentType = attachment.ContentType,
                sizeBytes = attachment.SizeBytes,
                tokenEstimate = attachment.TokenEstimate,
                s3Key = attachment.S3Key
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ATTACHMENT] Upload failed for {Filename}", file.FileName);
            return StatusCode(500, new { error = "Upload failed" });
        }
    }
}
