using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FortressAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FortressAI.Web.Data;
using Microsoft.Extensions.Configuration;

namespace FortressAI.Web.Controllers;

/// <summary>
/// REST endpoint for the Haven PWA to query FAIT's knowledge base.
/// Authenticated via API key (x-api-key header) — no browser/cookie session required.
/// POST /api/haven/chat
/// GET  /api/haven/kb-list
/// GET  /api/haven/project-list
/// </summary>
[ApiController]
[Route("api/haven")]
[Authorize(Policy = "ExcelAddinAccess")]
public class HavenChatController : ControllerBase
{
    private readonly KnowledgeBaseService _kbService;
    private readonly BedrockService _bedrockService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<HavenChatController> _logger;
    private readonly IConfiguration _configuration;

    public HavenChatController(
        KnowledgeBaseService kbService,
        BedrockService bedrockService,
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<HavenChatController> logger,
        IConfiguration configuration)
    {
        _kbService = kbService;
        _bedrockService = bedrockService;
        _dbFactory = dbFactory;
        _logger = logger;
        _configuration = configuration;
    }

    public sealed class HavenChatRequest
    {
        public string Message { get; set; } = "";
        /// <summary>Optional: FAIT project ID whose KB docs should be searched.</summary>
        public Guid? ProjectId { get; set; }
        /// <summary>Optional: conversation ID for history context (not yet used in v1).</summary>
        public Guid? ConversationId { get; set; }
        /// <summary>Optional: override which KB types to search. Values: "corp", "personal", "team".
        /// If null/empty, defaults to corp KB only — preserves existing Haven PWA behaviour.</summary>
        public List<string>? KbTypes { get; set; }
    }

    public sealed class HavenChatResponse
    {
        public string Answer { get; set; } = "";
        public List<string> Sources { get; set; } = new();
    }

    /// <summary>
    /// POST /api/haven/chat
    /// Accepts a user message, retrieves relevant KB chunks, synthesises an
    /// answer via Claude. Supports both buffered JSON (default) and SSE streaming
    /// (when client sends Accept: text/event-stream).
    /// </summary>
    [HttpPost("chat")]
    public async Task Chat(
        [FromBody] HavenChatRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("{\"error\":\"message is required\"}", ct);
            return;
        }

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var wantsStream = Request.Headers.Accept.ToString().Contains("text/event-stream");
        _logger.LogInformation("[Haven] Chat request from user={UserId} projectId={ProjectId} messageLen={Len} streaming={Streaming}",
            userIdStr, request.ProjectId, request.Message.Length, wantsStream);

        // ── KB Retrieval ──────────────────────────────────────────────────────────
        var kbChunks = new List<KbChunk>();
        var hasKbTypes = request.KbTypes != null && request.KbTypes.Count > 0;

        if (hasKbTypes)
        {
            // Explicit KB types requested
            foreach (var kbType in request.KbTypes!)
            {
                switch (kbType.ToLowerInvariant())
                {
                    case "corp":
                        try
                        {
                            var chunks = await _kbService.RetrieveCorpAsync(request.Message);
                            kbChunks.AddRange(chunks);
                            _logger.LogInformation("[Haven] Corp KB returned {Count} chunks", chunks.Count);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[Haven] Corp KB retrieval failed");
                        }
                        break;

                    case "personal":
                        if (Guid.TryParse(userIdStr, out var userId))
                        {
                            try
                            {
                                var chunks = await _kbService.RetrievePersonalAsync(request.Message, userId);
                                kbChunks.AddRange(chunks);
                                _logger.LogInformation("[Haven] Personal KB returned {Count} chunks", chunks.Count);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[Haven] Personal KB retrieval failed for user {UserId}", userId);
                            }
                        }
                        break;

                    case "team":
                        // Team KB requires a teamId — not available in Haven context yet. Skip with warning.
                        _logger.LogWarning("[Haven] Team KB requested but teamId is not available in Haven context — skipping");
                        break;

                    case "dev":
                        // Developer KB — retrieve from FORGE-DevTeam-Shared
                        try
                        {
                            var chunks = await _kbService.RetrieveDevAsync(request.Message);
                            kbChunks.AddRange(chunks);
                            _logger.LogInformation("[Haven] Dev KB returned {Count} chunks", chunks.Count);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[Haven] Dev KB retrieval failed");
                        }
                        break;

                    default:
                        _logger.LogWarning("[Haven] Unknown KB type requested: {KbType}", kbType);
                        break;
                }
            }
        }
        else
        {
            // Default behaviour: corp + project (if provided)
            try
            {
                var corpChunks = await _kbService.RetrieveCorpAsync(request.Message);
                kbChunks.AddRange(corpChunks);
                _logger.LogInformation("[Haven] Corp KB returned {Count} chunks", corpChunks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Haven] Corp KB retrieval failed");
            }
        }

        // Project KB is always added if ProjectId is set (regardless of KbTypes)
        if (request.ProjectId.HasValue)
        {
            try
            {
                var projectChunks = await _kbService.RetrieveProjectAsync(request.Message, request.ProjectId.Value);
                kbChunks.AddRange(projectChunks);
                _logger.LogInformation("[Haven] Project KB ({ProjectId}) returned {Count} chunks", request.ProjectId.Value, projectChunks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Haven] Project KB retrieval failed for project {ProjectId}", request.ProjectId.Value);
            }
        }

        // ── Build System Prompt ───────────────────────────────────────────────────────
        var systemPromptBuilder = new StringBuilder();
        systemPromptBuilder.AppendLine("You are Haven, a helpful assistant. Answer the user's question concisely and accurately based on the provided context.");
        systemPromptBuilder.AppendLine("If the answer is not in the context, say so clearly rather than guessing.");
        systemPromptBuilder.AppendLine();

        var sources = new List<string>();

        if (kbChunks.Count > 0)
        {
            systemPromptBuilder.AppendLine("## Retrieved Context");
            foreach (var chunk in kbChunks.OrderByDescending(c => c.Score).Take(8))
            {
                var sourceName = ExtractSourceName(chunk.Source);
                if (!string.IsNullOrEmpty(sourceName) && !sources.Contains(sourceName))
                    sources.Add(sourceName);

                systemPromptBuilder.AppendLine($"### Source: {sourceName}");
                systemPromptBuilder.AppendLine(chunk.Content);
                systemPromptBuilder.AppendLine();
            }
        }
        else
        {
            systemPromptBuilder.AppendLine("No specific context was retrieved. Answer based on your general knowledge if applicable.");
        }

        var messages = new List<MessageDto>
        {
            new() { Role = "user", Content = request.Message }
        };

        // ── SSE streaming path ────────────────────────────────────────────────────────
        if (wantsStream)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx buffering if any

            try
            {
                await foreach (var chunk in _bedrockService.StreamChatAsync(
                    messages,
                    systemPromptBuilder.ToString(),
                    "claude-sonnet-4-6",
                    maxTokens: 2048,
                    temperature: 0.3).WithCancellation(ct))
                {
                    if (chunk.Type == "text" && chunk.Text != null)
                        await Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk.Text)}\n\n", ct);
                }
                await Response.WriteAsync("data: [DONE]\n\n", ct);
            }
            catch (OperationCanceledException) { /* client disconnected — normal */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Haven] SSE streaming failed");
            }
            return;
        }

        // ── Buffered JSON path (original behaviour) ───────────────────────────────────
        var answerBuilder = new StringBuilder();
        try
        {
            await foreach (var chunk in _bedrockService.StreamChatAsync(
                messages,
                systemPromptBuilder.ToString(),
                "claude-sonnet-4-6",
                maxTokens: 2048,
                temperature: 0.3).WithCancellation(ct))
            {
                if (chunk.Type == "text" && chunk.Text != null)
                    answerBuilder.Append(chunk.Text);
            }
        }
        catch (OperationCanceledException)
        {
            Response.StatusCode = 499;
            await Response.WriteAsync("{\"error\":\"Request cancelled\"}", ct);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Haven] Bedrock streaming failed");
            Response.StatusCode = 502;
            await Response.WriteAsync("{\"error\":\"AI service unavailable\"}", ct);
            return;
        }

        var answer = answerBuilder.ToString().Trim();
        if (string.IsNullOrEmpty(answer))
        {
            _logger.LogWarning("[Haven] Bedrock returned empty answer");
            Response.StatusCode = 502;
            await Response.WriteAsync("{\"error\":\"AI service returned empty response\"}", ct);
            return;
        }

        _logger.LogInformation("[Haven] Answer generated: {Len} chars, {SourceCount} sources", answer.Length, sources.Count);

        Response.ContentType = "application/json";
        await Response.WriteAsync(
            JsonSerializer.Serialize(new HavenChatResponse { Answer = answer, Sources = sources }),
            ct);
    }

    // ── /api/haven/kb-search ──────────────────────────────────────────────────────

    public sealed class KbSearchRequest
    {
        public string Query { get; set; } = "";
        public Guid? ProjectId { get; set; }
        /// <summary>Optional: override which KB types to search. Values: "corp", "personal", "team".
        /// If null/empty, defaults to corp (existing behaviour).</summary>
        public List<string>? KbTypes { get; set; }
    }

    /// <summary>
    /// POST /api/haven/kb-search
    /// Returns raw KB chunks for a given query. Used by the Excel add-in "Ask FORGE" feature.
    /// </summary>
    [HttpPost("kb-search")]
    public async Task<IActionResult> KbSearch(
        [FromBody] KbSearchRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "query is required" });

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var chunks = new List<KbChunk>();
        var hasKbTypes = request.KbTypes != null && request.KbTypes.Count > 0;

        if (hasKbTypes)
        {
            foreach (var kbType in request.KbTypes!)
            {
                switch (kbType.ToLowerInvariant())
                {
                    case "corp":
                        try
                        {
                            var c = await _kbService.RetrieveCorpAsync(request.Query);
                            chunks.AddRange(c);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[Haven] KbSearch corp retrieval failed");
                        }
                        break;

                    case "personal":
                        if (Guid.TryParse(userIdStr, out var uid))
                        {
                            try
                            {
                                var c = await _kbService.RetrievePersonalAsync(request.Query, uid);
                                chunks.AddRange(c);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[Haven] KbSearch personal retrieval failed for user {UserId}", uid);
                            }
                        }
                        break;

                    case "team":
                        _logger.LogWarning("[Haven] KbSearch: Team KB requested but teamId not available — skipping");
                        break;

                    case "dev":
                        // Developer KB — retrieve from FORGE-DevTeam-Shared
                        try
                        {
                            var c = await _kbService.RetrieveDevAsync(request.Query);
                            chunks.AddRange(c);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[Haven] KbSearch dev retrieval failed");
                        }
                        break;
                }
            }
        }
        else
        {
            // Default: corp only
            try
            {
                var corpChunks = await _kbService.RetrieveCorpAsync(request.Query);
                chunks.AddRange(corpChunks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Haven] KbSearch corp retrieval failed");
            }
        }

        // Project KB always added if ProjectId provided
        if (request.ProjectId.HasValue)
        {
            try
            {
                var projChunks = await _kbService.RetrieveProjectAsync(request.Query, request.ProjectId.Value);
                chunks.AddRange(projChunks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Haven] KbSearch project retrieval failed for {ProjectId}", request.ProjectId.Value);
            }
        }

        var results = chunks
            .OrderByDescending(c => c.Score)
            .Take(5)
            .Select(c => new
            {
                content = c.Content,
                source = ExtractSourceName(c.Source),
                score = c.Score
            });

        return Ok(new { results });
    }

    // ── /api/haven/kb-list ────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/haven/kb-list
    /// Returns the configured KB types available for this deployment.
    /// </summary>
    [HttpGet("kb-list")]
    public IActionResult KbList()
    {
        var kbs = new[]
        {
            new
            {
                id       = "corp",
                name     = "Fortress Knowledge Base",
                type     = "corp",
                alwaysOn = false,
                available = !string.IsNullOrEmpty(_configuration["KnowledgeBase:CorpKbId"])
            },
            new
            {
                id       = "personal",
                name     = "My Knowledge Base",
                type     = "personal",
                alwaysOn = true,
                available = !string.IsNullOrEmpty(_configuration["KnowledgeBase:PersonalKbId"])
            },
            new
            {
                id       = "team",
                name     = "Team Knowledge Base",
                type     = "team",
                alwaysOn = false,
                available = !string.IsNullOrEmpty(_configuration["KnowledgeBase:TeamKbId"])
            },
            new
            {
                id       = "dev",
                name     = "Developer Knowledge Base",
                type     = "dev",
                alwaysOn = false,
                available = !string.IsNullOrEmpty(_configuration["KnowledgeBase:DevKbId"])
            },
        };

        // Only return KBs that are configured
        return Ok(new { kbs = kbs.Where(k => k.available) });
    }

    // ── /api/haven/project-list ───────────────────────────────────────────────────

    /// <summary>
    /// GET /api/haven/project-list
    /// Returns projects owned by the authenticated user.
    /// </summary>
    [HttpGet("project-list")]
    public async Task<IActionResult> ProjectList(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Ok(new { projects = Array.Empty<object>() });

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var projects = await db.Projects
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Name)
            .Select(p => new { id = p.Id, name = p.Name })
            .ToListAsync(ct);

        return Ok(new { projects });
    }

    /// <summary>Extracts a human-readable filename from an S3 URI or path.</summary>
    private static string ExtractSourceName(string source)
    {
        if (string.IsNullOrEmpty(source)) return "";
        // Handle s3:// URIs: s3://bucket/path/to/filename.md → filename.md
        var lastSlash = source.LastIndexOf('/');
        return lastSlash >= 0 ? source[(lastSlash + 1)..] : source;
    }
}
