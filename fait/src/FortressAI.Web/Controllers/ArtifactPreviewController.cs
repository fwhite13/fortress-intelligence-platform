using Microsoft.AspNetCore.Mvc;
using Amazon.S3;
using FortressAI.Web.Services;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;

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
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public ArtifactPreviewController(
        ArtifactPreviewService previewService,
        IDbContextFactory<AppDbContext> dbFactory,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<ArtifactPreviewController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _previewService = previewService;
        _dbFactory = dbFactory;
        _s3 = s3;
        _bucket = config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";
        _logger = logger;
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(
        Guid id,
        [FromQuery] string token,
        [FromQuery] long expires,
        [FromQuery] bool preview = false)
    {
        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { error = "Missing token" });

        // Fail immediately on expired token — before touching the DB
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now >= expires)
            return Unauthorized(new { error = "Invalid or expired token" });

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
            var s3KeyToFetch = preview && !string.IsNullOrEmpty(artifact.PreviewS3Key)
                ? artifact.PreviewS3Key
                : artifact.S3Key;
            var s3Response = await _s3.GetObjectAsync(_bucket, s3KeyToFetch);

            // Stream directly to response — no buffering
            Response.ContentType = preview && !string.IsNullOrEmpty(artifact.PreviewS3Key)
                ? "application/pdf"
                : (!string.IsNullOrEmpty(artifact.MimeType) ? artifact.MimeType : "application/octet-stream");
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.ContentLength = (preview && !string.IsNullOrEmpty(artifact.PreviewS3Key))
                ? s3Response.ContentLength
                : (artifact.SizeBytes > 0 ? artifact.SizeBytes : null);
            var cd = new System.Net.Http.Headers.ContentDispositionHeaderValue("inline");
            cd.FileNameStar = artifact.Filename;
            Response.Headers["Content-Disposition"] = cd.ToString();
            // Prevent caching of sensitive preview content
            Response.Headers["Cache-Control"] = "private, no-store";

            try
            {
                await s3Response.ResponseStream.CopyToAsync(Response.Body);
            }
            catch (Exception copyEx)
            {
                _logger.LogError(copyEx, "[ArtifactPreview] Stream copy failed mid-response for artifact {Id}", id);
                // Cannot change status code — response already started
            }
            return new EmptyResult();
        }
        catch (AmazonS3Exception s3Ex)
        {
            _logger.LogError(s3Ex, "[ArtifactPreview] S3 fetch failed for artifact {Id}, key={Key}, code={Code}",
                id, artifact.S3Key, s3Ex.ErrorCode);
            return StatusCode(502, new { error = "File unavailable" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ArtifactPreview] Unexpected error fetching artifact {Id}", id);
            return StatusCode(500, new { error = "Internal error" });
        }
    }

    private record ConvertPptxResult([property: JsonPropertyName("previewS3Key")] string? PreviewS3Key);

    [HttpPost("{id:guid}/convert-pptx")]
    [Authorize]
    public async Task<IActionResult> ConvertPptx(Guid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var artifact = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == id);
        if (artifact == null) return NotFound();

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (artifact.UserId.ToString() != userId) return Forbid();

        // Return cached key if already converted
        if (!string.IsNullOrEmpty(artifact.PreviewS3Key))
            return Ok(new { previewS3Key = artifact.PreviewS3Key });

        // Call dedicated converter service
        var converterBase = _config["CONVERTER_BASE_URL"] ?? "http://localhost:3001";
        var converterApiKey = _config["CONVERTER_API_KEY"];
        using var client = _httpClientFactory.CreateClient("HarnessClient"); // reuse 10-min timeout client
        if (!string.IsNullOrEmpty(converterApiKey))
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", converterApiKey);
        var body = new
        {
            artifactId = id.ToString(),
            s3Key = artifact.S3Key,
            userId = artifact.UserId.ToString(),
            outputBucket = _config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces"
        };
        var resp = await client.PostAsJsonAsync($"{converterBase}/convert", body);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("PPTX converter returned {status}", resp.StatusCode);
            return StatusCode(502, new { error = "Conversion failed" });
        }

        var result = await resp.Content.ReadFromJsonAsync<ConvertPptxResult>();
        if (result?.PreviewS3Key != null)
        {
            artifact.PreviewS3Key = result.PreviewS3Key;
            await db.SaveChangesAsync();
        }

        return Ok(new { previewS3Key = result?.PreviewS3Key });
    }

    [HttpGet("{id:guid}/preview-status")]
    [Authorize]
    public async Task<IActionResult> PreviewStatus(Guid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var artifact = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == id);
        if (artifact == null) return NotFound();

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (artifact.UserId.ToString() != userId) return Forbid();

        if (string.IsNullOrEmpty(artifact.PreviewS3Key))
            return Ok(new { status = "pending" });

        // Generate HMAC token for preview URL
        var (token, expires) = _previewService.GenerateToken(id, artifact.UserId);
        var previewUrl = $"/api/artifacts/{id}/preview?token={Uri.EscapeDataString(token)}&expires={expires}&preview=true";
        return Ok(new { status = "ready", previewUrl });
    }
}
