using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FortressAI.Web.Services;

[ApiController]
[Route("api/memory")]
public class MemoryImportController : ControllerBase
{
    private readonly IMemoryFileService _memoryFileService;
    private readonly IConfiguration _config;
    private readonly ILogger<MemoryImportController> _logger;

    public MemoryImportController(IMemoryFileService memoryFileService, IConfiguration config, ILogger<MemoryImportController> logger)
    {
        _memoryFileService = memoryFileService;
        _config = config;
        _logger = logger;
    }

    private bool IsInternalAuthorized()
    {
        var configToken = _config["INTERNAL_API_TOKEN"];
        if (string.IsNullOrEmpty(configToken)) return false;
        return Request.Headers.TryGetValue("X-Internal-Token", out var tok) && tok == configToken;
    }

    [HttpPost("import")]
    [AllowAnonymous]
    public async Task<IActionResult> ImportMemory([FromBody] ImportMemoryRequest request, CancellationToken ct)
    {
        if (!IsInternalAuthorized()) return Unauthorized(new { error = "Unauthorized" });
        if (!Guid.TryParse(request.UserId, out var userId))
            return BadRequest(new { error = "Invalid userId" });
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "content required" });

        try
        {
            var result = await _memoryFileService.ImportMemoryAsync(userId, request.Content, ct);
            return Ok(new { success = true, chunks = result.Chunks });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MemoryImport] Import failed for user {UserId}", userId);
            return Ok(new { success = false, error = ex.Message });
        }
    }
}

public record ImportMemoryRequest(string UserId, string Content);
