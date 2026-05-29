using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FortressAI.Web.Data;
using FortressAI.Web.Services;
using FortressAI.Shared.Models;
using System.Text.Json;
using System.IO.Compression;

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
    private readonly ForgeService _forgeService;

    public WorkspaceController(
        IWorkspaceFileService workspaceFileService,
        IDocumentGeneratorService documentGeneratorService,
        IWorkspaceUploadService uploadService,
        IDbContextFactory<AppDbContext> dbFactory,
        IConfiguration config,
        ILogger<WorkspaceController> logger,
        ForgeService forgeService)
    {
        _workspaceFileService = workspaceFileService;
        _documentGeneratorService = documentGeneratorService;
        _uploadService = uploadService;
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
        _forgeService = forgeService;
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
    /// Inserts a user_workspace_uploads row with source='assistant'.
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
        return Ok(folders.Select(f => new { f.Id, f.Name, f.S3Prefix, f.CreatedAt }));
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
        return Ok(new { folder.Id, folder.Name, folder.S3Prefix, folder.CreatedAt });
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

    [HttpPut("folders/{folderId}/rename")]
    [Authorize]
    public async Task<IActionResult> RenameFolder(Guid folderId, [FromBody] RenameFolderRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.NewName))
            return BadRequest(new { error = "NewName is required" });
        await using var db = await _dbFactory.CreateDbContextAsync();
        var folder = await db.WorkspaceFolders.FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId.Value);
        if (folder == null) return NotFound();
        folder.Name = request.NewName.Trim();
        await db.SaveChangesAsync();
        return Ok(new { folder.Id, folder.Name });
    }

    // ─── Internal API for harness ─────────────────────────────────────────────

    /// <summary>
    /// Called by harness to list workspace files/folders for a user.
    /// ADO#3301 — replaces direct DB connection in harness list_files tool.
    /// </summary>
    [HttpPost("internal/list-files")]
    [AllowAnonymous]
    public async Task<IActionResult> InternalListFiles([FromBody] InternalListFilesRequest request)
    {
        if (!IsInternalAuthorized())
            return Unauthorized(new { error = "Unauthorized" });

        if (!Guid.TryParse(request.UserId, out var userId))
            return BadRequest(new { error = "Invalid userId" });

        Guid? folderId = null;
        if (!string.IsNullOrEmpty(request.FolderId) && Guid.TryParse(request.FolderId, out var parsedFolderId))
            folderId = parsedFolderId;

        var folders = await _uploadService.GetAllFoldersAsync(userId); // all folders regardless of depth
        var files = await _uploadService.GetAllFilesAsync(userId);

        var items = folders
            .Select(f => new { name = f.Name, type = "folder" })
            .Concat<object>(files.Select(f => new { name = f.Filename, type = "file", size = f.SizeBytes, mimeType = f.MimeType }))
            .ToList();

        return Ok(new { items });
    }

    /// <summary>
    /// Called by harness to get authoritative KB access entitlements for a user.
    /// ADO#3309 — prevents Path B KB access bypass via model tool input.
    /// </summary>
    [HttpGet("internal/kb-access")]
    [AllowAnonymous]
    public async Task<IActionResult> GetKbAccess([FromQuery] string userId)
    {
        if (!IsInternalAuthorized())
            return Unauthorized(new { error = "Unauthorized" });

        if (!Guid.TryParse(userId, out var userGuid))
            return BadRequest(new { error = "Invalid userId" });

        try
        {
            var corpKbId = _config["KnowledgeBase:CorpKbId"] ?? "";
            var corpEnabled = !string.IsNullOrEmpty(corpKbId);

            var teams = await _forgeService.GetUserTeamsAsync(userGuid);
            var authorizedTeamIds = teams.Select(t => t.Id).ToList();

            return Ok(new
            {
                corpEnabled,
                personalUserId = userId,
                authorizedTeamIds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkspaceController] GetKbAccess failed for userId={UserId}", userId);
            return StatusCode(500, new { error = "Internal error" });
        }
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

    [HttpGet("files/{fileId}/versions")]
    [Authorize]
    public async Task<IActionResult> GetFileVersions(Guid fileId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        var versions = await _uploadService.GetFileVersionsAsync(userId.Value, fileId);
        return Ok(versions.Select(v => new { v.Id, v.VersionNumber, v.S3Key, v.SizeBytes, v.CreatedAt, v.CreatedBy }));
    }

    [HttpPut("files/{fileId}/rollback/{versionNumber}")]
    [Authorize]
    public async Task<IActionResult> RollbackFile(Guid fileId, int versionNumber)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        var file = await _uploadService.RollbackFileAsync(userId.Value, fileId, versionNumber);
        if (file == null) return NotFound();
        return Ok(new { file.Id, file.Filename, file.CurrentVersion, file.S3Key, file.SizeBytes });
    }

    [HttpPut("files/{fileId}/rename")]
    [Authorize]
    public async Task<IActionResult> RenameFile(Guid fileId, [FromBody] RenameFileRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.NewFilename))
            return BadRequest(new { error = "NewFilename is required" });
        var file = await _uploadService.RenameFileAsync(userId.Value, fileId, request.NewFilename);
        if (file == null) return NotFound();
        return Ok(new { file.Id, file.Filename });
    }

    [HttpPut("files/{fileId}/move")]
    [Authorize]
    public async Task<IActionResult> MoveFile(Guid fileId, [FromBody] MoveFileRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        var file = await _uploadService.MoveFileAsync(userId.Value, fileId, request.NewFolderId);
        if (file == null) return NotFound();
        return Ok(new { file.Id, file.FolderId });
    }

    [HttpDelete("files/bulk")]
    [Authorize]
    public async Task<IActionResult> BulkDeleteFiles([FromBody] BulkDeleteRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        if (request.FileIds == null || request.FileIds.Count == 0)
            return BadRequest(new { error = "FileIds is required" });
        var result = await _uploadService.BulkDeleteFilesAsync(userId.Value, request.FileIds);
        if (result.FailedIds.Count > 0)
        {
            return StatusCode(207, new {
                succeeded = result.Succeeded,
                failed = result.FailedIds,
                errors = result.Errors
            });
        }
        return Ok(new { deleted = result.Succeeded });
    }

    [HttpPost("upload-zip")]
    [Authorize]
    [RequestSizeLimit(524288000)] // 500MB
    public async Task<IActionResult> UploadZip([FromForm] IFormFile file, [FromForm] Guid? folderId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided" });

        if (file.Length > 524288000)
            return StatusCode(413, new { error = "ZIP file exceeds 500MB limit" });

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .zip files are accepted" });

        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;

        await using var zipStream = file.OpenReadStream();
        using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);

        // Resolve extraction root
        Guid extractRootId;
        if (folderId.HasValue)
        {
            extractRootId = folderId.Value;
        }
        else
        {
            // No folder specified — create a folder named after the ZIP file at root
            var zipFolderName = Path.GetFileNameWithoutExtension(file.FileName);
            var existingFolders = await _uploadService.GetFoldersAsync(userId.Value, null);
            var existingFolder = existingFolders.FirstOrDefault(f => f.Name == zipFolderName);
            if (existingFolder != null)
            {
                extractRootId = existingFolder.Id;
            }
            else
            {
                try
                {
                    extractRootId = (await _uploadService.CreateFolderAsync(userId.Value, zipFolderName, null)).Id;
                }
                catch (DbUpdateException dupEx) when (dupEx.InnerException?.Message.Contains("Duplicate entry") == true || dupEx.InnerException?.Message.Contains("duplicate") == true)
                {
                    _logger.LogWarning("[WorkspaceController] ZIP root folder already exists (race), fetching existing: {FolderName}", zipFolderName);
                    var fallback = (await _uploadService.GetFoldersAsync(userId.Value, null)).FirstOrDefault(f => f.Name == zipFolderName);
                    extractRootId = fallback?.Id ?? throw new InvalidOperationException($"Could not find or create ZIP root folder: {zipFolderName}");
                }
            }
        }

        foreach (var entry in archive.Entries)
        {
            // Skip directories (entries with trailing slash / empty name)
            if (string.IsNullOrEmpty(entry.Name)) continue;

            // Zip slip protection
            var entryPath = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (entryPath.Contains("..")) // Path.IsPathRooted is a no-op here (TrimStart already removed leading slash)
            {
                skipped++;
                _logger.LogWarning("[WorkspaceController] Zip slip attempt: {EntryPath}", entryPath);
                continue;
            }

            const long MaxEntryBytes = 52_428_800; // 50MB per entry uncompressed
            if (entry.Length > MaxEntryBytes)
            {
                skipped++;
                _logger.LogWarning("[WorkspaceController] ZIP entry too large ({Size} bytes), skipped: {EntryName}", entry.Length, entry.FullName);
                continue;
            }

            var mimeType = GetMimeTypeForFilename(entry.Name);

            await using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            await entryStream.CopyToAsync(ms);
            ms.Position = 0;

            var targetFolderId = await GetOrCreateFolderPathAsync(userId.Value, entry.FullName, extractRootId);
            await _uploadService.SaveUploadAsync(userId.Value, targetFolderId, entry.Name, mimeType, ms);
            extracted.Add(entryPath);
        }

        _logger.LogInformation("[WorkspaceController] ZIP extracted: {Count} files, {Skipped} skipped for user {UserId}",
            extracted.Count, skipped, userId);

        return Ok(new { filesExtracted = extracted.Count, skipped, paths = extracted });
    }

    private async Task<Guid> GetOrCreateFolderPathAsync(Guid userId, string entryFullName, Guid rootFolderId)
    {
        var dir = Path.GetDirectoryName(entryFullName.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        if (string.IsNullOrEmpty(dir)) return rootFolderId;

        var segments = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentId = rootFolderId;

        foreach (var segment in segments)
        {
            var existing = (await _uploadService.GetFoldersAsync(userId, currentId))
                .FirstOrDefault(f => f.Name == segment);
            if (existing != null)
            {
                currentId = existing.Id;
            }
            else
            {
                try
                {
                    var created = await _uploadService.CreateFolderAsync(userId, segment, currentId);
                    currentId = created.Id;
                }
                catch (DbUpdateException dupEx) when (dupEx.InnerException?.Message.Contains("Duplicate entry") == true || dupEx.InnerException?.Message.Contains("duplicate") == true)
                {
                    _logger.LogWarning("[WorkspaceController] ZIP subfolder already exists (race), fetching existing: {Segment}", segment);
                    var fallback = (await _uploadService.GetFoldersAsync(userId, currentId)).FirstOrDefault(f => f.Name == segment);
                    currentId = fallback?.Id ?? throw new InvalidOperationException($"Could not find or create ZIP subfolder: {segment}");
                }
            }
        }
        return currentId;
    }

    private static string GetMimeTypeForFilename(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
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

public record InternalListFilesRequest(string UserId, string? FolderId = null);

public record RenameFileRequest(string NewFilename);

public record MoveFileRequest(Guid? NewFolderId);

public record BulkDeleteRequest(List<Guid> FileIds);

public record RenameFolderRequest(string NewName);
