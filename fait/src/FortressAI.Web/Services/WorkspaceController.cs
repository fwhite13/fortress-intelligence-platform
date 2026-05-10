using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FortressAI.Web.Data;
using FortressAI.Web.Services;
using FortressAI.Shared.Models;
using System.Text.Json;

namespace FortressAI.Web.Controllers;

[ApiController]
[Route("api/workspace")]
public class WorkspaceController : ControllerBase
{
    private readonly IWorkspaceFileService _workspaceFileService;
    private readonly IDocumentGeneratorService _documentGeneratorService;
    private readonly IWorkspaceUploadService _uploadService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<WorkspaceController> _logger;

    public WorkspaceController(
        IWorkspaceFileService workspaceFileService,
        IDocumentGeneratorService documentGeneratorService,
        IWorkspaceUploadService uploadService,
        IDbContextFactory<AppDbContext> dbFactory,
        IConfiguration config,
        ILogger<WorkspaceController> logger)
    {
        _workspaceFileService = workspaceFileService;
        _documentGeneratorService = documentGeneratorService;
        _uploadService = uploadService;
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
    }

    private bool IsInternalAuthorized()
    {
        var token = _config["INTERNAL_API_TOKEN"];
        if (string.IsNullOrEmpty(token)) return false;
        return Request.Headers.TryGetValue("X-Internal-Token", out var header)
            && header.ToString() == token;
    }

    /// <summary>
    /// Called by harness after writing an artifact to S3.
    /// Inserts a user_workspace_files row.
    /// </summary>
    [HttpPost("save-artifact")]
    [AllowAnonymous]
    public async Task<IActionResult> SaveArtifact([FromBody] SaveArtifactRequest request)
    {
        if (!IsInternalAuthorized())
            return Unauthorized(new { error = "Unauthorized" });

        if (!Guid.TryParse(request.UserId, out var userId))
            return BadRequest(new { error = "Invalid userId" });
        if (!Guid.TryParse(request.ConversationId, out var conversationId))
            return BadRequest(new { error = "Invalid conversationId" });

        Guid? taskRunId = null;
        if (!string.IsNullOrEmpty(request.TaskRunId) && Guid.TryParse(request.TaskRunId, out var parsedTaskRunId))
            taskRunId = parsedTaskRunId;

        var payload = new ArtifactPayload(
            request.Filename,
            request.S3Key,
            request.MimeType,
            request.SizeBytes
        );

        var file = await _workspaceFileService.SaveArtifactAsync(userId, conversationId, taskRunId, payload);
        _logger.LogInformation("[WorkspaceController] Artifact saved: id={Id} filename={Filename}", file.Id, file.Filename);

        return Ok(new { id = file.Id, filename = file.Filename, s3Key = file.S3Key });
    }

    /// <summary>
    /// Called by harness to generate a document.
    /// Returns the raw bytes of the generated document.
    /// </summary>
    [HttpPost("generate-document")]
    [AllowAnonymous]
    public async Task<IActionResult> GenerateDocument([FromBody] GenerateDocumentRequest request)
    {
        if (!IsInternalAuthorized())
            return Unauthorized(new { error = "Unauthorized" });

        var sections = request.Sections?.Select(s => new DocumentSection(s.Heading, s.Content)).ToList()
            ?? new List<DocumentSection>();

        var docRequest = new DocumentGenerationRequest(request.Type, request.Title, sections);
        var bytes = await _documentGeneratorService.GenerateAsync(docRequest);
        return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }

    // ─── Folder CRUD ──────────────────────────────────────────────────────────

    [HttpGet("folders")]
    [Authorize]
    public async Task<IActionResult> GetFolders([FromQuery] Guid? parentId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        var folders = await _uploadService.GetFoldersAsync(userId.Value, parentId);
        return Ok(folders.Select(f => new { f.Id, f.Name, f.ParentId, f.CreatedAt }));
    }

    [HttpPost("folders")]
    [Authorize]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });
        var folder = await _uploadService.CreateFolderAsync(userId.Value, request.Name, request.ParentId);
        return Ok(new { folder.Id, folder.Name, folder.ParentId, folder.CreatedAt });
    }

    [HttpDelete("folders/{folderId}")]
    [Authorize]
    public async Task<IActionResult> DeleteFolder(Guid folderId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        await _uploadService.DeleteFolderAsync(userId.Value, folderId);
        return Ok(new { success = true });
    }

    // ─── File CRUD ────────────────────────────────────────────────────────────

    [HttpGet("files")]
    [Authorize]
    public async Task<IActionResult> GetFiles([FromQuery] Guid? folderId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        var files = await _uploadService.GetFilesAsync(userId.Value, folderId);
        return Ok(files.Select(f => new { f.Id, f.Filename, f.MimeType, f.SizeBytes, f.CreatedAt, f.FolderId }));
    }

    [HttpPost("upload")]
    [Authorize]
    [RequestSizeLimit(52428800)] // 50MB
    public async Task<IActionResult> UploadFile([FromForm] IFormFile file, [FromForm] Guid? folderId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided" });

        if (file.Length > 52428800)
            return StatusCode(413, new { error = "File exceeds 50MB limit" });

        await using var stream = file.OpenReadStream();
        var upload = await _uploadService.SaveUploadAsync(
            userId.Value, folderId,
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            stream);

        return Ok(new { upload.Id, upload.Filename, upload.MimeType, upload.SizeBytes, upload.CreatedAt });
    }

    [HttpDelete("files/{fileId}")]
    [Authorize]
    public async Task<IActionResult> DeleteFile(Guid fileId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        await _uploadService.DeleteFileAsync(userId.Value, fileId);
        return Ok(new { success = true });
    }

    [HttpGet("files/{fileId}/download")]
    [Authorize]
    public async Task<IActionResult> GetDownloadUrl(Guid fileId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.WorkspaceUploads.FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId.Value);
        if (file == null) return NotFound();

        var url = await _uploadService.GetPresignedUrlAsync(file.S3Key);
        return Ok(new { url });
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")
            ?? User.FindFirst("userId");
        if (claim == null) return null;
        if (Guid.TryParse(claim.Value, out var id)) return id;
        return null;
    }
}

public record SaveArtifactRequest(
    string UserId,
    string ConversationId,
    string? TaskRunId,
    string Filename,
    string S3Key,
    string MimeType,
    long SizeBytes
);

public record GenerateDocumentRequest(
    string Type,
    string Title,
    List<GenerateDocumentSection>? Sections
);

public record GenerateDocumentSection(string Heading, string Content);

public record CreateFolderRequest(string Name, Guid? ParentId);
