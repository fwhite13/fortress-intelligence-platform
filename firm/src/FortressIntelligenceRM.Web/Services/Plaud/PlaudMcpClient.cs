using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace FortressIntelligenceRM.Web.Services.Plaud;

/// <summary>Plaud identity from the get_current_user tool.</summary>
public record PlaudCurrentUser(string? Id, string? Email);

/// <summary>WI #9134 — one list_files entry. Name is never logged. Holds no audio URL.</summary>
public record PlaudFileInfo(string Id, string? Name, DateTime? StartAt, DateTime? CreatedAt, long? DurationMs);

/// <summary>A non-2xx answer from the MCP endpoint. Carries only the status, never the body.</summary>
public class PlaudMcpException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public PlaudMcpException(string message, HttpStatusCode? statusCode = null) : base(message) => StatusCode = statusCode;
}

/// <summary>
/// WI #9133 — minimal hand-rolled MCP client for Plaud's remote server (streamable HTTP, spec §4.1).
///
/// Why not the official ModelContextProtocol C# SDK: FIRM targets net8.0 and the SDK pulls in the
/// Microsoft.Extensions.* 9.x/10.x line (AI abstractions, hosting, logging), which would upgrade shared
/// framework-adjacent packages under a live service for two JSON-RPC calls. The SDK also owns its own
/// HttpClient/transport, which makes it harder to guarantee the "no logging handlers, never log bodies"
/// rule (spec §9.3). Plaud needs only initialize, notifications/initialized and tools/call, so a small
/// client over the named "PlaudMcp" HttpClient (logging removed in Program.cs) is the smaller risk.
///
/// Protocol: one session per run: initialize → notifications/initialized → tools/call × N, carrying
/// Mcp-Session-Id when issued; Accept both JSON and SSE and parse either a JSON body or SSE data: frames;
/// on a 404 for a stale session re-initialize once. Bearer token per call. Logs tool name, status and
/// latency only.
/// </summary>
public class PlaudMcpClient
{
    public const string HttpClientName = "PlaudMcp";
    private const string ProtocolVersion = "2025-06-18";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PlaudOptions _options;
    private readonly ILogger<PlaudMcpClient> _logger;

    public PlaudMcpClient(IHttpClientFactory httpClientFactory, IOptions<PlaudOptions> options, ILogger<PlaudMcpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>An open MCP session bound to one access token.</summary>
    public sealed class Session
    {
        internal Session(string accessToken) => AccessToken = accessToken;
        internal string AccessToken { get; }
        internal string? SessionId { get; set; }
        internal string NegotiatedVersion { get; set; } = ProtocolVersion;
        internal int NextId { get; set; } = 1;
    }

    public async Task<Session> OpenSessionAsync(string accessToken, CancellationToken ct = default)
    {
        var session = new Session(accessToken);
        await InitializeAsync(session, ct);
        return session;
    }

    /// <summary>initialize + tools/call get_current_user. Returns null fields when Plaud omits them.</summary>
    public async Task<PlaudCurrentUser> GetCurrentUserAsync(string accessToken, CancellationToken ct = default)
    {
        var session = await OpenSessionAsync(accessToken, ct);
        var result = await CallToolAsync(session, "get_current_user", new JsonObject(), ct);
        var payload = ExtractToolPayload(result);
        if (payload == null)
            return new PlaudCurrentUser(null, null);
        var email = FindString(payload, "email");
        var id = FindString(payload, "id") ?? FindString(payload, "user_id") ?? FindString(payload, "uid");
        return new PlaudCurrentUser(id, email);
    }

    // ── WI #9134: import tools ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// list_files for a date window (YYYY-MM-DD, server-side filter; timezone unverified, so callers
    /// overlap the window). Returns one page; <c>PageWasFull</c> tells the caller whether to ask for the next.
    /// Field names are read tolerantly (id/file_id, start_at/startAt, duration in ms).
    /// </summary>
    public async Task<(IReadOnlyList<PlaudFileInfo> Files, bool PageWasFull)> ListFilesAsync(Session session,
        DateOnly dateFrom, DateOnly dateTo, int page, int pageSize, CancellationToken ct = default)
    {
        var args = new JsonObject
        {
            ["date_from"] = dateFrom.ToString("yyyy-MM-dd"),
            ["date_to"] = dateTo.ToString("yyyy-MM-dd"),
            ["page"] = page,
            ["page_size"] = Math.Max(10, pageSize),
        };
        var payload = ExtractToolPayload(await CallToolAsync(session, "list_files", args, ct));
        var items = FindFileArray(payload);
        var files = new List<PlaudFileInfo>();
        foreach (var item in items)
        {
            if (ParseFileInfo(item) is { } f)
                files.Add(f);
        }
        return (files, items.Count >= Math.Max(10, pageSize));
    }

    /// <summary>
    /// get_file. Returns the raw payload so the caller can read the 24 h presigned audio URL into a
    /// local variable (<see cref="ReadPresignedUrl"/>) without it ever passing through a typed field.
    /// </summary>
    public async Task<JsonNode?> GetFilePayloadAsync(Session session, string fileId, CancellationToken ct = default)
        => ExtractToolPayload(await CallToolAsync(session, "get_file", new JsonObject { ["file_id"] = fileId }, ct));

    /// <summary>The presigned audio URL in a get_file payload, or null when the device has not synced audio yet.</summary>
    public static string? ReadPresignedUrl(JsonNode? getFilePayload)
    {
        foreach (var name in new[] { "presigned_url", "presignedUrl", "audio_url", "audioUrl", "download_url" })
        {
            var v = FindString(getFilePayload, name);
            if (v != null && v.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return v;
        }
        return null;
    }

    /// <summary>
    /// get_transcript, one page (D17 archive only; never shown, pushed or logged). Returns the raw text
    /// content of the tool result and the next cursor, if any.
    /// </summary>
    public async Task<(string? Text, string? NextCursor)> GetTranscriptPageAsync(Session session, string fileId,
        string? cursor, int limit, CancellationToken ct = default)
    {
        var args = new JsonObject
        {
            ["file_id"] = fileId,
            ["block"] = "transaction",
            ["limit"] = Math.Clamp(limit, 1, 500),
        };
        if (!string.IsNullOrEmpty(cursor))
            args["cursor"] = cursor;
        var result = await CallToolAsync(session, "get_transcript", args, ct);
        var text = ExtractToolText(result);
        string? next = null;
        try
        {
            var payload = ExtractToolPayload(result);
            next = (payload as JsonObject)?["next_cursor"]?.ToString()
                   ?? (payload as JsonObject)?["nextCursor"]?.ToString()
                   ?? FindString(payload, "next_cursor");
        }
        catch (PlaudMcpException) { throw; }
        catch (Exception) { /* no cursor → last page */ }
        return (text, string.IsNullOrWhiteSpace(next) ? null : next);
    }

    /// <summary>get_note (D17 archive only; never shown, pushed or logged). Raw text content of the tool result.</summary>
    public async Task<string?> GetNoteTextAsync(Session session, string fileId, CancellationToken ct = default)
        => ExtractToolText(await CallToolAsync(session, "get_note", new JsonObject { ["file_id"] = fileId }, ct));

    /// <summary>All text content items of a tools/call result, joined; null when there are none.</summary>
    public static string? ExtractToolText(JsonNode? result)
    {
        if (result == null)
            return null;
        if (result["isError"]?.GetValue<bool>() == true)
            throw new PlaudMcpException("MCP tool returned isError");
        if (result["content"] is not JsonArray content)
            return result["structuredContent"]?.ToJsonString();
        var sb = new StringBuilder();
        foreach (var item in content)
        {
            if (item?["type"]?.GetValue<string>() != "text")
                continue;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(item["text"]?.GetValue<string>());
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>The list of file objects in a list_files payload: the payload itself if it is an array,
    /// else the first array under files/items/data/list/records/results, else any array of objects with an id.</summary>
    private static List<JsonNode> FindFileArray(JsonNode? payload)
    {
        if (payload is JsonArray direct)
            return direct.Where(n => n != null).Select(n => n!).ToList();
        if (payload is not JsonObject obj)
            return new List<JsonNode>();
        foreach (var key in new[] { "files", "items", "data", "list", "records", "results" })
        {
            if (obj[key] is JsonArray arr)
                return arr.Where(n => n != null).Select(n => n!).ToList();
            if (obj[key] is JsonObject inner && FindFileArray(inner) is { Count: > 0 } nested)
                return nested;
        }
        foreach (var (_, value) in obj)
        {
            if (value is JsonArray arr && arr.FirstOrDefault() is JsonObject first
                && (first.ContainsKey("id") || first.ContainsKey("file_id")))
                return arr.Where(n => n != null).Select(n => n!).ToList();
        }
        return new List<JsonNode>();
    }

    private static PlaudFileInfo? ParseFileInfo(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;
        var id = TopString(obj, "id", "file_id", "fileId");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
            return null;
        return new PlaudFileInfo(
            id,
            TopString(obj, "name", "title", "filename"),
            ParseTimestamp(obj["start_at"] ?? obj["startAt"] ?? obj["start_time"]),
            ParseTimestamp(obj["created_at"] ?? obj["createdAt"] ?? obj["create_time"]),
            ParseLong(obj["duration"] ?? obj["duration_ms"]));
    }

    private static string? TopString(JsonObject obj, params string[] names)
    {
        foreach (var n in names)
            if (obj[n] is JsonValue v && v.ToString() is { Length: > 0 } s)
                return s;
        return null;
    }

    private static long? ParseLong(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        return long.TryParse(v.ToString(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : null;
    }

    /// <summary>Epoch ms, epoch s, or ISO 8601 (format unverified, spec §4.1) → UTC.</summary>
    public static DateTime? ParseTimestamp(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (ParseLong(node) is long n && v.ToString().All(c => char.IsDigit(c) || c == '.'))
        {
            if (n <= 0) return null;
            // > 1e11 cannot be seconds before year 5138: treat as milliseconds.
            return n > 100_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(n).UtcDateTime
                : DateTimeOffset.FromUnixTimeSeconds(n).UtcDateTime;
        }
        return DateTimeOffset.TryParse(v.ToString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var dto)
            ? dto.UtcDateTime
            : null;
    }

    /// <summary>tools/call; re-initializes once if the server forgot the session (404).</summary>
    public async Task<JsonNode?> CallToolAsync(Session session, string toolName, JsonObject arguments, CancellationToken ct = default)
    {
        var parameters = new JsonObject { ["name"] = toolName, ["arguments"] = arguments };
        try
        {
            return await RequestAsync(session, "tools/call", parameters, toolName, ct);
        }
        catch (PlaudMcpException ex) when (ex.StatusCode == HttpStatusCode.NotFound && session.SessionId != null)
        {
            session.SessionId = null;
            await InitializeAsync(session, ct);
            return await RequestAsync(session, "tools/call", (JsonObject)parameters.DeepClone(), toolName, ct);
        }
    }

    private async Task InitializeAsync(Session session, CancellationToken ct)
    {
        var parameters = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "FIRM", ["version"] = "1.0" },
        };
        var result = await RequestAsync(session, "initialize", parameters, "initialize", ct);
        if (result?["protocolVersion"]?.GetValue<string>() is { Length: > 0 } negotiated)
            session.NegotiatedVersion = negotiated;

        // Notification: no id, server answers 202 Accepted with no body.
        using var req = BuildRequest(session, new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
        using var res = await Http().SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
            throw new PlaudMcpException("MCP notifications/initialized failed", res.StatusCode);
    }

    private async Task<JsonNode?> RequestAsync(Session session, string method, JsonObject parameters, string label, CancellationToken ct)
    {
        var id = session.NextId++;
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
        var sw = Stopwatch.StartNew();
        using var req = BuildRequest(session, message);
        using var res = await Http().SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        _logger.LogInformation("[PlaudMcp] {Call} status={Status} ms={Ms}", label, (int)res.StatusCode, sw.ElapsedMilliseconds);

        if (!res.IsSuccessStatusCode)
            throw new PlaudMcpException($"MCP {label} failed", res.StatusCode);

        if (method == "initialize" && res.Headers.TryGetValues("Mcp-Session-Id", out var sids))
            session.SessionId = sids.FirstOrDefault();

        var mediaType = res.Content.Headers.ContentType?.MediaType ?? "";
        JsonNode? response = mediaType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase)
            ? await ReadSseResponseAsync(res, id, ct)
            : JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));

        if (response == null)
            throw new PlaudMcpException($"MCP {label} returned no response");
        if (response["error"] is JsonNode error)
        {
            // JSON-RPC error code only; the message may echo user data.
            var code = error["code"]?.ToString() ?? "?";
            throw new PlaudMcpException($"MCP {label} JSON-RPC error {code}");
        }
        return response["result"];
    }

    private static async Task<JsonNode?> ReadSseResponseAsync(HttpResponseMessage res, int id, CancellationToken ct)
    {
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null || line.Length == 0)
            {
                // End of an event: dispatch it.
                if (data.Length > 0)
                {
                    JsonNode? node = null;
                    try { node = JsonNode.Parse(data.ToString()); } catch (JsonException) { }
                    data.Clear();
                    if (node is JsonObject obj && obj["id"]?.ToString() == id.ToString()
                        && (obj.ContainsKey("result") || obj.ContainsKey("error")))
                        return node;
                }
                if (line == null)
                    return null;
                continue;
            }
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart(' '));
            }
            // id:, event:, retry: and comments are ignored.
        }
    }

    private HttpRequestMessage BuildRequest(Session session, JsonObject message)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, _options.McpUrl)
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (session.SessionId != null)
            req.Headers.TryAddWithoutValidation("Mcp-Session-Id", session.SessionId);
        if (message["method"]?.GetValue<string>() != "initialize")
            req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", session.NegotiatedVersion);
        return req;
    }

    private HttpClient Http() => _httpClientFactory.CreateClient(HttpClientName);

    /// <summary>
    /// A tools/call result as JSON: structuredContent when present, else the first JSON object found in
    /// the text content. Plaud wraps payloads in &lt;untrusted-user-data-…&gt; tags, so the extraction is tolerant.
    /// </summary>
    public static JsonNode? ExtractToolPayload(JsonNode? result)
    {
        if (result == null)
            return null;
        if (result["isError"]?.GetValue<bool>() == true)
            throw new PlaudMcpException("MCP tool returned isError");
        if (result["structuredContent"] is JsonNode structured)
            return structured;
        if (result["content"] is not JsonArray content)
            return null;
        foreach (var item in content)
        {
            if (item?["type"]?.GetValue<string>() != "text")
                continue;
            var text = item["text"]?.GetValue<string>();
            if (string.IsNullOrEmpty(text))
                continue;
            var start = text.IndexOfAny(new[] { '{', '[' });
            var end = text.LastIndexOfAny(new[] { '}', ']' });
            if (start < 0 || end <= start)
                continue;
            try
            {
                return JsonNode.Parse(text.Substring(start, end - start + 1));
            }
            catch (JsonException)
            {
                // Not JSON; try the next content item.
            }
        }
        return null;
    }

    /// <summary>Depth-first search for the first string/number property with this name.</summary>
    public static string? FindString(JsonNode? node, string name)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && value is JsonValue v)
                    {
                        var s = v.ToString();
                        if (!string.IsNullOrWhiteSpace(s))
                            return s;
                    }
                }
                foreach (var (_, value) in obj)
                    if (FindString(value, name) is { } nested)
                        return nested;
                return null;
            case JsonArray arr:
                foreach (var item in arr)
                    if (FindString(item, name) is { } nested)
                        return nested;
                return null;
            default:
                return null;
        }
    }
}
