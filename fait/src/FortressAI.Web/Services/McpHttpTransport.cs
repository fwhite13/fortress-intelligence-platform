using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FortressAI.Shared.Models;
using FortressAI.Web.Services.Mcp;

namespace FortressAI.Web.Services;

public class McpHttpTransport
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpHttpTransport> _logger;

    public McpHttpTransport(IHttpClientFactory httpClientFactory, ILogger<McpHttpTransport> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<JsonElement> CallToolAsync(
        string endpointUrl,
        string toolName,
        JsonElement arguments,
        string? bearerToken = null,
        string? apiKey = null,
        CancellationToken ct = default)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = arguments
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
        {
            Content = JsonContent.Create(request)
        };

        if (bearerToken is not null)
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (apiKey is not null)
            httpRequest.Headers.Add("X-API-Key", apiKey);
        httpRequest.Headers.Add("Accept", "application/json");

        var http = _httpClientFactory.CreateClient("mcp-transport");
        var response = await http.SendAsync(httpRequest, ct);
        var rawBody = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("[McpTransport] CallTool {Url} → {Status} | body[0..300]: {Body}",
            endpointUrl, (int)response.StatusCode,
            rawBody.Length > 300 ? rawBody[..300] : rawBody);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(rawBody);

        if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null && error.ValueKind != JsonValueKind.Undefined)
        {
            var msg = error.TryGetProperty("message", out var m) ? m.GetString() : "MCP error";
            throw new McpToolException(msg ?? "MCP error");
        }

        if (doc.RootElement.TryGetProperty("result", out var result))
            return result.Clone();

        return doc.RootElement.Clone();
    }

    public async Task<List<McpToolDefinition>> ListToolsAsync(
        string endpointUrl,
        string? bearerToken = null,
        string? apiKey = null,
        CancellationToken ct = default)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "tools/list",
            @params = new { }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
        {
            Content = JsonContent.Create(request)
        };

        if (bearerToken is not null)
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (apiKey is not null)
            httpRequest.Headers.Add("X-API-Key", apiKey);
        httpRequest.Headers.Add("Accept", "application/json");

        var http = _httpClientFactory.CreateClient("mcp-transport");
        var response = await http.SendAsync(httpRequest, ct);
        var rawBody = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("[McpTransport] ListTools {Url} → {Status} | body[0..300]: {Body}",
            endpointUrl, (int)response.StatusCode,
            rawBody.Length > 300 ? rawBody[..300] : rawBody);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(rawBody);

        if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null && error.ValueKind != JsonValueKind.Undefined)
        {
            var msg = error.TryGetProperty("message", out var m) ? m.GetString() : "MCP error";
            throw new McpToolException(msg ?? "MCP error");
        }

        var tools = new List<McpToolDefinition>();
        if (doc.RootElement.TryGetProperty("result", out var result) &&
            result.TryGetProperty("tools", out var toolsArray))
        {
            foreach (var tool in toolsArray.EnumerateArray())
            {
                var def = new McpToolDefinition
                {
                    Name = tool.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Description = tool.TryGetProperty("description", out var d) ? d.GetString() : null,
                    InputSchema = tool.TryGetProperty("inputSchema", out var schema) ? schema.Clone() : JsonDocument.Parse("{}").RootElement
                };
                tools.Add(def);
            }
        }
        return tools;
    }
}
