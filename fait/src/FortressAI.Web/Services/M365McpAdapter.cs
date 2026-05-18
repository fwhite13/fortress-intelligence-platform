using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FortressAI.Web.Services.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FortressAI.Web.Services;

/// <summary>
/// Internal MCP adapter for Microsoft 365 Graph API tools.
/// Mirrors the DevOpsMcpAdapter pattern exactly.
///
/// Endpoint: POST /internal/mcp/m365
/// Auth:     Loopback-only (same-process HttpClient calls from McpToolService).
///           The caller passes the userId as X-API-Key so this adapter can
///           look up the user's Graph access token via MicrosoftTokenService.
///
/// DevOps wiring reference:
///   - Registered in DatabaseInitializationService (same INSERT pattern as DevOps row)
///   - McpToolService.ExecuteToolAsync routes m365__<tool> → this endpoint via McpHttpTransport
///   - GetConversationToolsAsync gates on UserMicrosoftTokens token existence
///   - GetActiveServersForUserAsync filters m365 server: only visible when user is connected
/// </summary>
[ApiController]
public class M365McpAdapter : ControllerBase
{
    private readonly MicrosoftTokenService _tokenService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<M365McpAdapter> _logger;

    public M365McpAdapter(MicrosoftTokenService tokenService, IHttpClientFactory httpClientFactory, ILogger<M365McpAdapter> logger)
    {
        _tokenService = tokenService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("/internal/mcp/m365")]
    public async Task<IActionResult> HandleMcpRequest([FromBody] McpCallRequest request)
    {
        // Restrict to loopback only — internal same-process endpoint (mirrors DevOpsMcpAdapter)
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
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
                result = new
                {
                    tools = new object[]
                    {
                        new { name = "list_emails",           description = "List recent emails from inbox",                inputSchema = JsonDocument.Parse(@"{""type"":""object"",""properties"":{""top"":{""type"":""integer"",""default"":10},""filter"":{""type"":""string""}}}").RootElement },
                        new { name = "get_email",             description = "Get full content of a specific email by ID",  inputSchema = JsonDocument.Parse(@"{""type"":""object"",""properties"":{""messageId"":{""type"":""string""}},""required"":[""messageId""]}").RootElement },
                        new { name = "send_email",            description = "Send an email",                               inputSchema = JsonDocument.Parse(@"{""type"":""object"",""properties"":{""to"":{""type"":""string""},""subject"":{""type"":""string""},""body"":{""type"":""string""}},""required"":[""to"",""subject"",""body""]}").RootElement },
                        new { name = "list_calendar_events",  description = "List upcoming calendar events",               inputSchema = JsonDocument.Parse(@"{""type"":""object"",""properties"":{""top"":{""type"":""integer"",""default"":10},""startDateTime"":{""type"":""string""},""endDateTime"":{""type"":""string""}}}").RootElement },
                        new { name = "create_calendar_event", description = "Create a calendar event",                    inputSchema = JsonDocument.Parse(@"{""type"":""object"",""properties"":{""subject"":{""type"":""string""},""start"":{""type"":""string""},""end"":{""type"":""string""},""body"":{""type"":""string""},""attendees"":{""type"":""array"",""items"":{""type"":""string""}}},""required"":[""subject"",""start"",""end""]}").RootElement }
                    }
                }
            });
        }

        if (request.Method != "tools/call")
            return BadRequest(new { error = new { code = -32601, message = "Method not found" } });

        // Extract userId from X-API-Key header (McpToolService passes the userId string as the api_key)
        var userIdStr = HttpContext.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(new { error = new { code = 401, message = "Invalid or missing user identity" } });
        }

        // Get access token — refresh if expired (MicrosoftTokenService handles refresh automatically)
        var accessToken = await _tokenService.GetValidAccessTokenAsync(userId);
        _logger.LogInformation("[M365] Token lookup: userId={UserId} tokenPresent={Present}", userId, accessToken != null);
        if (accessToken == null)
        {
            return Ok(new McpCallResponse
            {
                Jsonrpc = "2.0",
                Id = request.Id,
                Result = new McpToolResultContent
                {
                    Content = new List<McpContentBlock>
                    {
                        new McpContentBlock { Type = "text", Text = "No valid Microsoft 365 token for this user. Please connect your Microsoft account in Settings." }
                    },
                    IsError = true
                }
            });
        }

        var toolName = request.Params?.Name ?? "";
        var args = request.Params?.Arguments ?? default;

        _logger.LogInformation("[M365] Tool dispatch: userId={UserId} tool={Tool}", userId, toolName);

        try
        {
            var result = toolName switch
            {
                "list_emails"           => await ListEmails(accessToken, args),
                "get_email"             => await GetEmail(accessToken, args),
                "send_email"            => await SendEmail(accessToken, args),
                "list_calendar_events"  => await ListCalendarEvents(accessToken, args),
                "create_calendar_event" => await CreateCalendarEvent(accessToken, args),
                _                       => null
            };

            if (result is null)
                return BadRequest(new { error = new { code = -32601, message = $"Unknown tool: {toolName}" } });

            return Ok(new McpCallResponse
            {
                Jsonrpc = "2.0",
                Id = request.Id,
                Result = new McpToolResultContent
                {
                    Content = new List<McpContentBlock>
                    {
                        new McpContentBlock { Type = "text", Text = result }
                    },
                    IsError = false
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[M365] Tool call failed: {ToolName}", toolName);
            return Ok(new McpCallResponse
            {
                Jsonrpc = "2.0",
                Id = request.Id,
                Result = new McpToolResultContent
                {
                    Content = new List<McpContentBlock>
                    {
                        new McpContentBlock { Type = "text", Text = $"Microsoft 365 tool error: {ex.Message}" }
                    },
                    IsError = true
                }
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool implementations — Graph API calls
    // ─────────────────────────────────────────────────────────────────

    private async Task<string?> ListEmails(string accessToken, JsonElement input)
    {
        var top = input.TryGetProperty("top", out var t) ? t.GetInt32() : 10;
        var filter = input.TryGetProperty("filter", out var f) ? f.GetString() : null;
        var url = $"https://graph.microsoft.com/v1.0/me/messages?$top={top}&$select=id,subject,from,receivedDateTime,bodyPreview,isRead";
        if (!string.IsNullOrEmpty(filter)) url += $"&$filter={Uri.EscapeDataString(filter)}";
        return await CallGraph(accessToken, HttpMethod.Get, url);
    }

    private async Task<string?> GetEmail(string accessToken, JsonElement input)
    {
        var messageId = input.GetProperty("messageId").GetString()!;
        var url = $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(messageId)}?$select=id,subject,from,toRecipients,body,receivedDateTime";
        return await CallGraph(accessToken, HttpMethod.Get, url);
    }

    private async Task<string?> SendEmail(string accessToken, JsonElement input)
    {
        var to = input.GetProperty("to").GetString()!;
        var subject = input.GetProperty("subject").GetString()!;
        var body = input.GetProperty("body").GetString()!;
        var payload = new
        {
            message = new
            {
                subject,
                body = new { contentType = "Text", content = body },
                toRecipients = new[] { new { emailAddress = new { address = to } } }
            }
        };
        var url = "https://graph.microsoft.com/v1.0/me/sendMail";
        var result = await CallGraph(accessToken, HttpMethod.Post, url, JsonSerializer.Serialize(payload));
        // sendMail returns 202 with empty body on success
        return result ?? "Email sent successfully.";
    }

    private async Task<string?> ListCalendarEvents(string accessToken, JsonElement input)
    {
        var top = input.TryGetProperty("top", out var t) ? t.GetInt32() : 10;
        var start = input.TryGetProperty("startDateTime", out var s) ? s.GetString() : DateTime.UtcNow.ToString("o");
        var end = input.TryGetProperty("endDateTime", out var e) ? e.GetString() : DateTime.UtcNow.AddDays(7).ToString("o");
        var url = $"https://graph.microsoft.com/v1.0/me/calendarView?startDateTime={Uri.EscapeDataString(start!)}&endDateTime={Uri.EscapeDataString(end!)}&$top={top}&$select=id,subject,start,end,location,organizer,bodyPreview";
        return await CallGraph(accessToken, HttpMethod.Get, url);
    }

    private async Task<string?> CreateCalendarEvent(string accessToken, JsonElement input)
    {
        var subject = input.GetProperty("subject").GetString()!;
        var startDt = input.GetProperty("start").GetString()!;
        var endDt = input.GetProperty("end").GetString()!;
        var bodyContent = input.TryGetProperty("body", out var b) ? b.GetString() : null;
        string[]? attendeeAddresses = null;
        if (input.TryGetProperty("attendees", out var att) && att.ValueKind == JsonValueKind.Array)
            attendeeAddresses = att.EnumerateArray().Select(a => a.GetString()!).ToArray();

        // Build payload as a serializable object
        var payloadObj = new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["start"] = new { dateTime = startDt, timeZone = "UTC" },
            ["end"] = new { dateTime = endDt, timeZone = "UTC" }
        };
        if (bodyContent != null)
            payloadObj["body"] = new { contentType = "Text", content = bodyContent };
        if (attendeeAddresses != null)
            payloadObj["attendees"] = attendeeAddresses.Select(a => new { emailAddress = new { address = a }, type = "required" }).ToArray();

        var url = "https://graph.microsoft.com/v1.0/me/events";
        return await CallGraph(accessToken, HttpMethod.Post, url, JsonSerializer.Serialize(payloadObj));
    }

    private async Task<string?> CallGraph(string accessToken, HttpMethod method, string url, string? body = null)
    {
        var client = _httpClientFactory.CreateClient("graph");
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        if (body != null)
            req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var resp = await client.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            _logger.LogWarning("[M365] Graph API error {Status} for {Url}: {Body}", resp.StatusCode, url, json.Length > 500 ? json[..500] : json);
        return string.IsNullOrEmpty(json) ? null : json;
    }
}
