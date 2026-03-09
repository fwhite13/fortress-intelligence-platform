using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortressAI.Web.Services.Mcp;

// MCP JSON-RPC request/response types
public class McpCallParams
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}

public class McpCallRequest
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public McpCallParams? Params { get; set; }
}

public class McpContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public class McpToolResultContent
{
    [JsonPropertyName("content")]
    public List<McpContentBlock> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}

public class McpCallResponse
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("result")]
    public McpToolResultContent? Result { get; set; }

    [JsonPropertyName("error")]
    public McpErrorInfo? Error { get; set; }
}

public class McpErrorInfo
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

// Tool service DTOs
public class BedrockToolSpec
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InputSchemaJson { get; set; } = "{}";
}

public record AvailableTool(
    string FullName,
    string DisplayName,
    string? Description,
    System.Text.Json.JsonElement InputSchema,
    Guid ServerId
);

public record McpToolResult(
    bool Success,
    string? Content,
    string? ErrorMessage,
    int LatencyMs
);

// Accumulates streaming tool_input_delta chunks
public class ToolCallAccumulator
{
    public string ToolUseId { get; }
    public string ToolName { get; }
    private readonly System.Text.StringBuilder _inputBuffer = new();

    public ToolCallAccumulator(string toolUseId, string toolName)
    {
        ToolUseId = toolUseId;
        ToolName = toolName;
    }

    public void AppendInput(string delta) => _inputBuffer.Append(delta);

    public string GetInputJson() => _inputBuffer.ToString();

    public JsonElement GetInputElement()
    {
        var json = _inputBuffer.ToString();
        if (string.IsNullOrEmpty(json)) return JsonDocument.Parse("{}").RootElement;
        try { return JsonDocument.Parse(json).RootElement; }
        catch { return JsonDocument.Parse("{}").RootElement; }
    }
}

// Registry DTOs
public class CreateMcpServerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string TransportType { get; set; } = "http";
    public string? EndpointUrl { get; set; }
    public string AuthType { get; set; } = "none";
    public bool RequiresUserAuth { get; set; }
    public string? SystemApiKey { get; set; }
    public string? AuthConfigJson { get; set; }
    public string? OAuthClientSecret { get; set; }
    public int RateLimitPerMinute { get; set; } = 30;
}

public class UpdateMcpServerRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? EndpointUrl { get; set; }
    public string? AuthType { get; set; }
    public bool? RequiresUserAuth { get; set; }
    public string? SystemApiKey { get; set; }
    public string? AuthConfigJson { get; set; }
    public string? OAuthClientSecret { get; set; }
    public int? RateLimitPerMinute { get; set; }
}

public class McpServerConnectionStatus
{
    public FortressAI.Shared.Models.McpServer Server { get; set; } = null!;
    public bool IsConnected { get; set; }
    public string? ConnectedAs { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class McpToolException : Exception
{
    public McpToolException(string message) : base(message) { }
    public McpToolException(string message, Exception inner) : base(message, inner) { }
}

public record OAuthTokenResult(string AccessToken, string? RefreshToken, DateTime? ExpiresAt, string? Scopes);
