using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FortressAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FortressAI.Web.Data;

namespace FortressAI.Web.Controllers;

/// <summary>
/// REST endpoint for the Haven PWA to query FAIT's knowledge base.
/// Authenticated via API key (x-api-key header) — no browser/cookie session required.
/// POST /api/haven/chat
/// </summary>
[ApiController]
[Route("api/haven")]
[Authorize(AuthenticationSchemes = "AppKeyAuth", Policy = "AppKeyOnly")]
public class HavenChatController : ControllerBase
{
    private readonly KnowledgeBaseService _kbService;
    private readonly BedrockService _bedrockService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<HavenChatController> _logger;

    public HavenChatController(
        KnowledgeBaseService kbService,
        BedrockService bedrockService,
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<HavenChatController> logger)
    {
        _kbService = kbService;
        _bedrockService = bedrockService;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public sealed class HavenChatRequest
    {
        public string Message { get; set; } = "";
        /// <summary>Optional: FAIT project ID whose KB docs should be searched.</summary>
        public Guid? ProjectId { get; set; }
        /// <summary>Optional: conversation ID for history context (not yet used in v1).</summary>
        public Guid? ConversationId { get; set; }
    }

    public sealed class HavenChatResponse
    {
        public string Answer { get; set; } = "";
        public List<string> Sources { get; set; } = new();
    }

    /// <summary>
    /// POST /api/haven/chat
    /// Accepts a user message, retrieves relevant KB chunks (project + corp), synthesises an
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

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var wantsStream = Request.Headers.Accept.ToString().Contains("text/event-stream");
        _logger.LogInformation("[Haven] Chat request from user={UserId} projectId={ProjectId} messageLen={Len} streaming={Streaming}",
            userId, request.ProjectId, request.Message.Length, wantsStream);

        // ── KB Retrieval ──────────────────────────────────────────────────────────────
        var kbChunks = new List<KbChunk>();

        // If a projectId was provided, search that project's KB first
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

        // Also pull from corp KB for general context
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

        var chunks = new List<KbChunk>();

        try
        {
            var corpChunks = await _kbService.RetrieveCorpAsync(request.Query);
            chunks.AddRange(corpChunks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Haven] KbSearch corp retrieval failed");
        }

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

    /// <summary>Extracts a human-readable filename from an S3 URI or path.</summary>
    private static string ExtractSourceName(string source)
    {
        if (string.IsNullOrEmpty(source)) return "";
        // Handle s3:// URIs: s3://bucket/path/to/filename.md → filename.md
        var lastSlash = source.LastIndexOf('/');
        return lastSlash >= 0 ? source[(lastSlash + 1)..] : source;
    }
}
