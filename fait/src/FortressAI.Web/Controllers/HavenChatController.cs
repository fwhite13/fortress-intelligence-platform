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
    /// answer via Claude, and returns it as plain JSON.  SSE streaming is a future enhancement.
    /// </summary>
    [HttpPost("chat")]
    public async Task<IActionResult> Chat(
        [FromBody] HavenChatRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "message is required" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _logger.LogInformation("[Haven] Chat request from user={UserId} projectId={ProjectId} messageLen={Len}",
            userId, request.ProjectId, request.Message.Length);

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

        // ── Generate Answer via Bedrock ───────────────────────────────────────────────
        var messages = new List<MessageDto>
        {
            new() { Role = "user", Content = request.Message }
        };

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
            return StatusCode(499, new { error = "Request cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Haven] Bedrock streaming failed");
            return StatusCode(502, new { error = "AI service unavailable" });
        }

        var answer = answerBuilder.ToString().Trim();
        if (string.IsNullOrEmpty(answer))
        {
            _logger.LogWarning("[Haven] Bedrock returned empty answer");
            return StatusCode(502, new { error = "AI service returned empty response" });
        }

        _logger.LogInformation("[Haven] Answer generated: {Len} chars, {SourceCount} sources", answer.Length, sources.Count);

        return Ok(new HavenChatResponse
        {
            Answer = answer,
            Sources = sources
        });
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
