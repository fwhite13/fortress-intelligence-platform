using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using FortressAI.Web.Services.Mcp;

namespace FortressAI.Web.Services;

public interface IMcpToolService
{
    Task<List<AvailableTool>> GetConversationToolsAsync(Guid conversationId, Guid userId);
    Task<List<McpServer>> GetActiveServersForUserAsync(Guid userId);
    Task<McpToolResult> ExecuteToolAsync(
        Guid userId,
        Guid conversationId,
        string toolName,
        JsonElement toolInput,
        CancellationToken ct = default);
}

public class McpToolService : IMcpToolService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IMcpConnectionService _connectionService;
    private readonly McpHttpTransport _transport;
    private readonly IMemoryCache _cache;
    private readonly ILogger<McpToolService> _logger;
    private const int DefaultRateLimitPerMinute = 30;

    public McpToolService(
        IDbContextFactory<AppDbContext> dbFactory,
        IMcpConnectionService connectionService,
        McpHttpTransport transport,
        IMemoryCache cache,
        ILogger<McpToolService> logger)
    {
        _dbFactory = dbFactory;
        _connectionService = connectionService;
        _transport = transport;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<AvailableTool>> GetConversationToolsAsync(Guid conversationId, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Get servers explicitly enabled for this conversation
        var enabledServerIds = await db.ConversationMcpServers
            .Where(cms => cms.ConversationId == conversationId && cms.Enabled)
            .Select(cms => cms.ServerId)
            .ToListAsync();

        // Get all active servers that don't require user auth (auto-available)
        var autoServers = await db.McpServers
            .Where(s => s.IsActive && !s.RequiresUserAuth)
            .ToListAsync();

        // Get explicitly enabled servers (that require user auth)
        var enabledServers = enabledServerIds.Count > 0
            ? await db.McpServers
                .Where(s => s.IsActive && enabledServerIds.Contains(s.Id))
                .ToListAsync()
            : new List<McpServer>();

        // Combine, deduplicate
        var allServers = autoServers
            .Concat(enabledServers)
            .DistinctBy(s => s.Id)
            .ToList();

        var tools = new List<AvailableTool>();
        foreach (var server in allServers)
        {
            if (string.IsNullOrEmpty(server.ToolManifestJson)) continue;

            List<McpToolDefinition>? defs;
            try { defs = JsonSerializer.Deserialize<List<McpToolDefinition>>(server.ToolManifestJson); }
            catch { continue; }

            if (defs is null) continue;

            foreach (var def in defs)
            {
                var fullName = $"{server.Slug}__{def.Name}";
                // Bedrock tool name limit is 64 chars
                if (fullName.Length > 64) fullName = fullName[..64];
                tools.Add(new AvailableTool(
                    fullName,
                    def.Name,
                    def.Description,
                    def.InputSchema,
                    server.Id
                ));
            }
        }
        return tools;
    }

    public async Task<List<McpServer>> GetActiveServersForUserAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var servers = await db.McpServers
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();

        // Only show user-auth servers (e.g. OAuth) if the user has a connected token
        var result = new List<McpServer>();
        foreach (var server in servers)
        {
            if (server.RequiresUserAuth)
            {
                var connected = await db.UserMcpTokens
                    .AnyAsync(t => t.ServerId == server.Id && t.UserId == userId);
                if (connected) result.Add(server);
            }
            else
            {
                result.Add(server);
            }
        }
        return result;
    }

    public async Task<McpToolResult> ExecuteToolAsync(
        Guid userId,
        Guid conversationId,
        string toolName,
        JsonElement toolInput,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Parse slug and actual tool name from namespaced name
        var separatorIdx = toolName.IndexOf("__", StringComparison.Ordinal);
        if (separatorIdx < 0)
            return new McpToolResult(false, null, $"Invalid tool name format: {toolName}", 0);

        var slug = toolName[..separatorIdx];
        var actualToolName = toolName[(separatorIdx + 2)..];

        await using var db = await _dbFactory.CreateDbContextAsync();
        var server = await db.McpServers.FirstOrDefaultAsync(s => s.Slug == slug && s.IsActive, ct);
        if (server is null)
            return new McpToolResult(false, null, $"MCP server '{slug}' not found or inactive", 0);

        // Rate limiting (per-server configurable limit)
        if (!CheckRateLimit(userId, slug, server.RateLimitPerMinute))
        {
            // Log the rate-limited attempt for observability
            var rateLimitLog = new McpToolCallLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ConversationId = conversationId,
                ServerId = server.Id,
                ToolName = actualToolName,
                InputJson = toolInput.GetRawText().Length > 2000 ? toolInput.GetRawText()[..2000] : toolInput.GetRawText(),
                Status = "rate_limited",
                ErrorMessage = "Rate limit exceeded",
                LatencyMs = 0,
                CreatedAt = DateTime.UtcNow
            };
            db.McpToolCallLogs.Add(rateLimitLog);
            try { await db.SaveChangesAsync(ct); } catch { /* best effort */ }

            return new McpToolResult(false, null, $"Rate limit exceeded for {slug} (max {server.RateLimitPerMinute}/min)", 0);
        }

        // Get access token
        var accessToken = await _connectionService.GetAccessTokenAsync(userId, server.Id);

        // Log the call
        var logEntry = new McpToolCallLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConversationId = conversationId,
            ServerId = server.Id,
            ToolName = actualToolName,
            InputJson = toolInput.GetRawText().Length > 2000 ? toolInput.GetRawText()[..2000] : toolInput.GetRawText(),
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            var endpointUrl = server.EndpointUrl ?? throw new InvalidOperationException("Server has no endpoint URL");

            JsonElement result;
            if (server.AuthType == "api_key" && !string.IsNullOrEmpty(accessToken))
                result = await _transport.CallToolAsync(endpointUrl, actualToolName, toolInput, apiKey: accessToken, ct: ct);
            else if (!string.IsNullOrEmpty(accessToken))
                result = await _transport.CallToolAsync(endpointUrl, actualToolName, toolInput, bearerToken: accessToken, ct: ct);
            else
                result = await _transport.CallToolAsync(endpointUrl, actualToolName, toolInput, ct: ct);

            sw.Stop();

            // Extract text content from MCP result
            string? content = null;
            if (result.TryGetProperty("content", out var contentArray))
            {
                var textParts = new System.Text.StringBuilder();
                foreach (var block in contentArray.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var text))
                        textParts.Append(text.GetString());
                }
                content = textParts.ToString();
            }
            else
            {
                content = result.GetRawText();
            }

            // Update log
            logEntry.Status = "success";
            logEntry.OutputJson = content?.Length > 2000 ? content[..2000] : content;
            logEntry.LatencyMs = (int)sw.ElapsedMilliseconds;
            db.McpToolCallLogs.Add(logEntry);
            await db.SaveChangesAsync(ct);

            return new McpToolResult(true, content, null, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Tool execution failed: {ToolName} on {Slug}", actualToolName, slug);

            logEntry.Status = ex is TaskCanceledException ? "timeout" : "error";
            logEntry.ErrorMessage = ex.Message;
            logEntry.LatencyMs = (int)sw.ElapsedMilliseconds;
            db.McpToolCallLogs.Add(logEntry);
            try { await db.SaveChangesAsync(CancellationToken.None); } catch { /* best effort */ }

            return new McpToolResult(false, null, $"Tool execution failed: {ex.Message}", (int)sw.ElapsedMilliseconds);
        }
    }

    private bool CheckRateLimit(Guid userId, string slug, int limitPerMinute)
    {
        var key = $"mcp_rate:{userId}:{slug}";
        var count = _cache.Get<int>(key);
        if (count >= limitPerMinute) return false;
        _cache.Set(key, count + 1, TimeSpan.FromMinutes(1));
        return true;
    }
}
