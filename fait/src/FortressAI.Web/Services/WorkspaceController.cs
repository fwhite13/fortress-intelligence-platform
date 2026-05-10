using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FortressAI.Web.Services;
using System.Text.Json;

namespace FortressAI.Web.Controllers;

[ApiController]
[Route("api/workspace")]
public class WorkspaceController : ControllerBase
{
    private readonly IWorkspaceFileService _workspaceFileService;
    private readonly IDocumentGeneratorService _documentGeneratorService;
    private readonly IConfiguration _config;
    private readonly ILogger<WorkspaceController> _logger;

    public WorkspaceController(
        IWorkspaceFileService workspaceFileService,
        IDocumentGeneratorService documentGeneratorService,
        IConfiguration config,
        ILogger<WorkspaceController> logger)
    {
        _workspaceFileService = workspaceFileService;
        _documentGeneratorService = documentGeneratorService;
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
