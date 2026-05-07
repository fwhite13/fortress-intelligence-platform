using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

/// <summary>
/// Design Agent service — dispatches Stitch MCP tool calls via the Fargate harness.
/// Falls back to CC-native HTML generation when Stitch is unavailable.
/// Artifacts are saved to S3 under workspaces/{userId}/artifacts/design/{sessionId}/.
/// </summary>
public class DesignAgentService : IDesignAgentService
{
    private readonly IUserAgentRuntime _runtime;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<DesignAgentService> _logger;
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    private string Bucket => _config["AWS:WorkspaceBucket"]
        ?? throw new InvalidOperationException("AWS:WorkspaceBucket is not configured.");

    public DesignAgentService(
        IUserAgentRuntime runtime,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<DesignAgentService> logger,
        IDbContextFactory<FaitV2DbContext> dbFactory,
        IHttpClientFactory httpClientFactory)
    {
        _runtime = runtime;
        _s3 = s3;
        _config = config;
        _logger = logger;
        _dbFactory = dbFactory;
        _httpClientFactory = httpClientFactory;
    }

    // ─── GenerateScreenAsync ──────────────────────────────────────────────────

    public async Task<DesignAgentResult> GenerateScreenAsync(
        string userId,
        string prompt,
        string? designDnaContext = null,
        CancellationToken ct = default)
    {
        // Persist session before generating
        var sessionId = Guid.NewGuid().ToString();
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            var session = new DesignAgentSession
            {
                Id = sessionId,
                UserId = userId,
                StitchProjectId = null,
                DesignDna = designDnaContext,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.DesignAgentSessions.Add(session);
            await db.SaveChangesAsync(ct);
        }

        if (!await IsStitchAvailableAsync(userId, ct))
        {
            _logger.LogInformation("Stitch unavailable for user {UserId} — using CC-native HTML fallback", userId);
            var fallbackHtml = await GenerateFallbackHtmlAsync(userId, prompt, ct);
            return new DesignAgentResult(fallbackHtml, ScreenId: null, ProjectId: null, IsFallback: true, SessionId: sessionId);
        }

        try
        {
            var args = new Dictionary<string, object>
            {
                ["prompt"] = prompt
            };
            if (!string.IsNullOrEmpty(designDnaContext))
                args["design_dna"] = designDnaContext;

            var resultJson = await _runtime.DispatchToolCallAsync(userId, "stitch_generate_screen", args, ct);
            var result = JsonSerializer.Deserialize<StitchGenerateResult>(resultJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var html = result?.Html ?? string.Empty;
            _logger.LogInformation("Stitch generated screen for user {UserId}, screenId={ScreenId}", userId, result?.ScreenId);
            return new DesignAgentResult(html, result?.ScreenId, result?.ProjectId, IsFallback: false, SessionId: sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stitch GenerateScreen failed for user {UserId} — falling back to CC-native", userId);
            var fallbackHtml = await GenerateFallbackHtmlAsync(userId, prompt, ct);
            return new DesignAgentResult(fallbackHtml, ScreenId: null, ProjectId: null, IsFallback: true, SessionId: sessionId);
        }
    }

    // ─── ExtractDesignContextAsync ────────────────────────────────────────────

    public async Task<string> ExtractDesignContextAsync(
        string userId,
        string screenHtmlOrImageBase64,
        CancellationToken ct = default)
    {
        if (!await IsStitchAvailableAsync(userId, ct))
        {
            _logger.LogDebug("Stitch unavailable — returning empty design DNA for user {UserId}", userId);
            return string.Empty;
        }

        try
        {
            var args = new Dictionary<string, object>
            {
                ["content"] = screenHtmlOrImageBase64
            };
            var resultJson = await _runtime.DispatchToolCallAsync(userId, "stitch_extract_design_dna", args, ct);
            _logger.LogInformation("Extracted design DNA for user {UserId}", userId);
            return resultJson;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stitch ExtractDesignContext failed for user {UserId}", userId);
            return string.Empty;
        }
    }

    // ─── RefineScreenAsync ────────────────────────────────────────────────────

    public async Task<DesignAgentResult> RefineScreenAsync(
        string userId,
        string existingScreenId,
        string refinementPrompt,
        CancellationToken ct = default)
    {
        if (!await IsStitchAvailableAsync(userId, ct))
        {
            _logger.LogInformation("Stitch unavailable — using CC-native HTML fallback for refinement, user {UserId}", userId);
            var fallbackHtml = await GenerateFallbackHtmlAsync(userId, refinementPrompt, ct);
            return new DesignAgentResult(fallbackHtml, ScreenId: null, ProjectId: null, IsFallback: true);
        }

        try
        {
            var args = new Dictionary<string, object>
            {
                ["screen_id"] = existingScreenId,
                ["prompt"] = refinementPrompt
            };
            var resultJson = await _runtime.DispatchToolCallAsync(userId, "stitch_refine_screen", args, ct);
            var result = JsonSerializer.Deserialize<StitchGenerateResult>(resultJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var html = result?.Html ?? string.Empty;
            _logger.LogInformation("Stitch refined screen for user {UserId}, screenId={ScreenId}", userId, result?.ScreenId);
            return new DesignAgentResult(html, result?.ScreenId, result?.ProjectId, IsFallback: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stitch RefineScreen failed for user {UserId} — falling back to CC-native", userId);
            var fallbackHtml = await GenerateFallbackHtmlAsync(userId, refinementPrompt, ct);
            return new DesignAgentResult(fallbackHtml, ScreenId: null, ProjectId: null, IsFallback: true);
        }
    }

    // ─── SaveArtifactAsync ────────────────────────────────────────────────────

    public async Task<string> SaveArtifactAsync(
        string userId,
        string sessionId,
        string html,
        string artifactName,
        string? stitchScreenId = null,
        bool isFallback = false,
        CancellationToken ct = default)
    {
        var safeName = string.Concat(artifactName.Split(System.IO.Path.GetInvalidFileNameChars()));
        var key = $"workspaces/{userId}/artifacts/design/{sessionId}/{safeName}.html";

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            ContentBody = html,
            ContentType = "text/html"
        }, ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.DesignAgentArtifacts.Add(new DesignAgentArtifact
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            UserId = userId,
            ArtifactName = artifactName,
            S3Key = key,
            StitchScreenId = stitchScreenId,
            IsFallback = isFallback,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Saved design artifact {Key} for user {UserId}", key, userId);
        return key;
    }

    // ─── IsStitchAvailableAsync ───────────────────────────────────────────────

    public async Task<bool> IsStitchAvailableAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var session = await _runtime.GetSessionAsync(userId, ct);
            if (session == null || session.Status != RuntimeSessionStatus.Running || string.IsNullOrEmpty(session.PrivateIp))
                return false;

            var client = _httpClientFactory.CreateClient("HarnessClient");
            client.Timeout = TimeSpan.FromSeconds(5);
            var resp = await client.GetAsync($"http://{session.PrivateIp}:{session.Port}/tools/stitch/health", ct);
            if (!resp.IsSuccessStatusCode) return false;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("available", out var avail) && avail.GetBoolean();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stitch health check failed for user {UserId} — treating as unavailable", userId);
            return false;
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<string> GenerateFallbackHtmlAsync(string userId, string prompt, CancellationToken ct)
    {
        // Ask the Fargate harness to generate HTML natively via Claude Code
        try
        {
            var turnRequest = new TurnRequest(
                Message: $"Generate a complete, styled HTML page for the following design request. Return only the HTML markup with embedded CSS, no markdown, no explanation:\n\n{prompt}",
                SystemPrompt: "You are a UI design assistant. Generate clean, modern HTML/CSS only. Return raw HTML with embedded styles."
            );

            var sb = new System.Text.StringBuilder();
            await foreach (var evt in _runtime.SendTurnAsync(userId, turnRequest, ct))
            {
                if (evt.Type == "text" && evt.Content != null)
                    sb.Append(evt.Content);
                else if (evt.Type is "done" or "error")
                    break;
            }

            var raw = sb.ToString().Trim();

            // Strip markdown code fences if CC returned them
            if (raw.StartsWith("```html", StringComparison.OrdinalIgnoreCase))
                raw = raw["```html".Length..].TrimStart();
            if (raw.StartsWith("```"))
                raw = raw[3..].TrimStart();
            if (raw.EndsWith("```"))
                raw = raw[..^3].TrimEnd();

            return raw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CC-native HTML fallback failed for user {UserId}", userId);
            return $"<html><body><p>Design generation failed. Please try again.</p></body></html>";
        }
    }

    // ─── Internal DTO ─────────────────────────────────────────────────────────

    private sealed record StitchGenerateResult(
        string? Html,
        string? ScreenId,
        string? ProjectId
    );
}
