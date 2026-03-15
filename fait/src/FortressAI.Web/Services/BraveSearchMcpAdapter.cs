using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FortressAI.Web.Services.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FortressAI.Web.Services;

[ApiController]
public class BraveSearchMcpAdapter : ControllerBase
{
    private readonly BraveSearchClient _braveClient;
    private readonly ILogger<BraveSearchMcpAdapter> _logger;

    // Brave web_search tool manifest
    private static readonly string BraveToolManifest = JsonSerializer.Serialize(new[]
    {
        new
        {
            name = "web_search",
            description = "Search the web for current, relevant information",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""query"": { ""type"": ""string"", ""description"": ""The search query"" },
                ""count"": { ""type"": ""integer"", ""description"": ""Number of results (1-10)"", ""default"": 5 }
              },
              ""required"": [""query""]
            }").RootElement
        }
    });

    public BraveSearchMcpAdapter(BraveSearchClient braveClient, ILogger<BraveSearchMcpAdapter> logger)
    {
        _braveClient = braveClient;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("/internal/mcp/brave")]
    public async Task<IActionResult> HandleMcpRequest([FromBody] McpCallRequest request)
    {
        // Restrict to loopback only — this endpoint is internal (same-process HttpClient calls).
        // Any non-loopback caller gets 403 immediately, preventing unauthenticated quota drain.
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        // Map IPv4-mapped IPv6 (::ffff:127.x.x.x) to IPv4 for IsLoopback check
        if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();
        if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
            return StatusCode(403, new { error = new { code = 403, message = "Forbidden: internal endpoint" } });

        if (request.Method == "tools/list")
        {
            return Ok(new
            {
                jsonrpc = "2.0",
                id = request.Id,
                result = new { tools = JsonDocument.Parse(BraveToolManifest).RootElement }
            });
        }

        if (request.Method == "tools/call" && request.Params?.Name == "web_search")
        {
            try
            {
                var args = request.Params.Arguments;
                var query = args.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
                var count = args.TryGetProperty("count", out var c) ? c.GetInt32() : 5;

                if (string.IsNullOrEmpty(query))
                    return BadRequest(new { error = new { code = -32602, message = "query is required" } });

                var results = await _braveClient.SearchAsync(query, count);
                var formatted = _braveClient.FormatResults(results);

                return Ok(new McpCallResponse
                {
                    Jsonrpc = "2.0",
                    Id = request.Id,
                    Result = new McpToolResultContent
                    {
                        Content = new List<McpContentBlock>
                        {
                            new McpContentBlock { Type = "text", Text = formatted }
                        },
                        IsError = false
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brave search failed");
                return Ok(new McpCallResponse
                {
                    Jsonrpc = "2.0",
                    Id = request.Id,
                    Result = new McpToolResultContent
                    {
                        Content = new List<McpContentBlock>
                        {
                            new McpContentBlock { Type = "text", Text = $"Search failed: {ex.Message}" }
                        },
                        IsError = true
                    }
                });
            }
        }

        return BadRequest(new { error = new { code = -32601, message = "Method not found" } });
    }
}
