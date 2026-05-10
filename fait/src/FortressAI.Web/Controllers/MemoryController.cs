using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FortressAI.Web.Services;

[ApiController]
[Route("api/memory")]
public class MemoryController : ControllerBase
{
    private readonly IMemoryFileService _memoryFileService;
    private readonly IConfiguration _config;
    private readonly ILogger<MemoryController> _logger;

    public MemoryController(IMemoryFileService memoryFileService, IConfiguration config, ILogger<MemoryController> logger)
    {
        _memoryFileService = memoryFileService;
        _config = config;
        _logger = logger;
    }

    private bool IsInternalAuthorized()
    {
        var configToken = _config["INTERNAL_API_TOKEN"];
        if (string.IsNullOrEmpty(configToken)) return false;
        return Request.Headers.TryGetValue("X-Internal-Token", out var tok) &&
               tok == configToken;
    }

    [HttpPost("read")]
    [AllowAnonymous]
    public async Task<IActionResult> ReadTopic([FromBody] ReadTopicRequest request)
    {
        if (!IsInternalAuthorized()) return Unauthorized(new { error = "Unauthorized" });
        if (!Guid.TryParse(request.UserId, out var userId))
            return BadRequest(new { error = "Invalid userId" });
        if (string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest(new { error = "slug required" });

        var content = await _memoryFileService.GetTopicContentAsync(userId, request.Slug);
        if (content == null)
            return Ok(new { found = false, content = (string?)null });
        return Ok(new { found = true, content });
    }

    [HttpPost("write")]
    [AllowAnonymous]
    public async Task<IActionResult> WriteTopic([FromBody] WriteTopicRequest request)
    {
        if (!IsInternalAuthorized()) return Unauthorized(new { error = "Unauthorized" });
        if (!Guid.TryParse(request.UserId, out var userId))
            return BadRequest(new { error = "Invalid userId" });
        if (string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest(new { error = "slug required" });
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "content required" });

        var title = string.IsNullOrWhiteSpace(request.Title) ? request.Slug : request.Title;

        await _memoryFileService.WriteTopicAsync(userId, request.Slug, title, request.Content);
        return Ok(new { success = true });
    }
}

public record ReadTopicRequest(string UserId, string Slug);
public record WriteTopicRequest(string UserId, string Slug, string? Title, string Content);
