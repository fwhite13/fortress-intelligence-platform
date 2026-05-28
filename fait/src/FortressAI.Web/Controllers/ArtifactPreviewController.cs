using Microsoft.AspNetCore.Mvc;
using Amazon.S3;
using FortressAI.Web.Services;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Controllers;

[ApiController]
[Route("api/artifacts")]
public class ArtifactPreviewController : ControllerBase
{
    private readonly ArtifactPreviewService _previewService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<ArtifactPreviewController> _logger;

    public ArtifactPreviewController(
        ArtifactPreviewService previewService,
        IDbContextFactory<AppDbContext> dbFactory,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<ArtifactPreviewController> logger)
    {
        _previewService = previewService;
        _dbFactory = dbFactory;
        _s3 = s3;
        _bucket = config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";
        _logger = logger;
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(
        Guid id,
        [FromQuery] string token,
        [FromQuery] long expires)
    {
        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { error = "Missing token" });

        // We need the userId to validate the token. We'll look up the artifact first,
        // then validate that the token's userId matches the artifact owner.
        // To do this without a 2-pass query, we extract userId from the artifact record
        // and validate it against the token.

        await using var db = await _dbFactory.CreateDbContextAsync();
        var artifact = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == id);

        if (artifact == null)
        {
            _logger.LogDebug("[ArtifactPreview] Artifact {Id} not found", id);
            return NotFound();
        }

        // Validate token against artifact's owner userId
        if (!_previewService.ValidateToken(id, artifact.UserId, token, expires))
        {
            _logger.LogWarning("[ArtifactPreview] Invalid or expired token for artifact {Id}", id);
            return Unauthorized(new { error = "Invalid or expired token" });
        }

        try
        {
            var s3Response = await _s3.GetObjectAsync(_bucket, artifact.S3Key);

            // Stream directly to response — no buffering
            Response.ContentType = artifact.MimeType;
            Response.ContentLength = artifact.SizeBytes > 0 ? artifact.SizeBytes : null;
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{artifact.Filename}\"";
            // Prevent caching of sensitive preview content
            Response.Headers["Cache-Control"] = "private, no-store";

            await s3Response.ResponseStream.CopyToAsync(Response.Body);
            return new EmptyResult();
        }
        catch (AmazonS3Exception s3Ex)
        {
            _logger.LogError(s3Ex, "[ArtifactPreview] S3 fetch failed for artifact {Id}, key={Key}, code={Code}",
                id, artifact.S3Key, s3Ex.ErrorCode);
            return StatusCode(502, new { error = "File unavailable" });
        }
    }
}
