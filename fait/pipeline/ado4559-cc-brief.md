# CC Brief: ADO4559 — WebFetchClient + MCP web_fetch Endpoint + Harness Prompt Update

## Working Directory
`/home/fredw/projects/fip/fait/src/FortressAI.Web/`

All code changes are in this project unless otherwise noted.

## Overview
Add a native `web_fetch` MCP tool to FAIT so the AI assistant can fetch and read the full content of a specific URL.
- Feature 13.1: `WebFetchClient` service (new C# service)
- Feature 13.2: `/internal/mcp/webfetch` endpoint in Program.cs + seed row in `DatabaseInitializationService`
- Feature 13.3: Harness system prompt + CLAUDE.md update

**DO NOT TOUCH:**
- `BraveSearchClient.cs`
- Brave Search registration in `Program.cs`
- Any Brave-related `appsettings.json` entries

---

## Step 1: Add NuGet Package

Add `HtmlAgilityPack` to `FortressAI.Web.csproj`:

File: `/home/fredw/projects/fip/fait/src/FortressAI.Web/FortressAI.Web.csproj`

Add inside `<ItemGroup>` that has other PackageReference entries:
```xml
<PackageReference Include="HtmlAgilityPack" Version="1.11.*" />
```

---

## Step 2: Create IWebFetchClient interface and WebFetchClient implementation

Create new file: `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/WebFetchClient.cs`

```csharp
using HtmlAgilityPack;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FortressAI.Web.Services;

public interface IWebFetchClient
{
    Task<WebFetchResult> FetchAsync(string url, CancellationToken ct = default);
}

public record WebFetchResult(
    bool Success,
    string? Title,
    string? MarkdownContent,
    string? ErrorMessage,
    bool IsJsRendered
);

public class WebFetchClient : IWebFetchClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebFetchClient> _logger;
    private const int MaxResponseBytes = 2 * 1024 * 1024; // 2MB
    private const int JsRenderThreshold = 200;

    public WebFetchClient(IHttpClientFactory httpClientFactory, ILogger<WebFetchClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<WebFetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            var handler = new HttpClientHandler
            {
                MaxAutomaticRedirections = 3,
                AllowAutoRedirect = true,
            };

            var client = _httpClientFactory.CreateClient("WebFetch");

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return new WebFetchResult(false, null, null,
                    $"HTTP {(int)response.StatusCode} {response.StatusCode} fetching {url}", false);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var isHtml = contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);

            // Read up to MaxResponseBytes
            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[MaxResponseBytes + 1];
            var bytesRead = 0;
            int read;
            var truncated = false;
            while ((read = await stream.ReadAsync(buffer.AsMemory(bytesRead, Math.Min(4096, buffer.Length - bytesRead)), cts.Token)) > 0)
            {
                bytesRead += read;
                if (bytesRead >= MaxResponseBytes)
                {
                    truncated = true;
                    break;
                }
            }

            var rawHtml = Encoding.UTF8.GetString(buffer, 0, Math.Min(bytesRead, MaxResponseBytes));

            // Parse with HtmlAgilityPack
            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);

            // Extract title
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            var title = titleNode != null ? WebUtility.HtmlDecode(titleNode.InnerText.Trim()) : null;

            // Remove noise nodes
            var noiseSelectors = new[] { "script", "style", "nav", "footer", "aside", "header", "form" };
            foreach (var selector in noiseSelectors)
            {
                var nodes = doc.DocumentNode.SelectNodes($"//{selector}");
                if (nodes != null)
                {
                    foreach (var node in nodes.ToList())
                        node.Remove();
                }
            }

            // Find main content node — prefer <main>, <article>, [role=main], fall back to <body>
            HtmlNode? contentNode =
                doc.DocumentNode.SelectSingleNode("//main") ??
                doc.DocumentNode.SelectSingleNode("//article") ??
                doc.DocumentNode.SelectSingleNode("//*[@role='main']") ??
                doc.DocumentNode.SelectSingleNode("//body");

            var markdown = contentNode != null
                ? ConvertToMarkdown(contentNode)
                : string.Empty;

            if (truncated)
                markdown += "\n\n*[Note: Response was truncated at 2MB limit.]*";

            // JS detection heuristic
            var isJsRendered = isHtml && markdown.Trim().Length < JsRenderThreshold;

            return new WebFetchResult(true, title, markdown, null, isJsRendered);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new WebFetchResult(false, null, null, $"Request timed out after 10 seconds fetching {url}", false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebFetchClient error fetching {Url}", url);
            return new WebFetchResult(false, null, null, $"Error fetching {url}: {ex.Message}", false);
        }
    }

    private static string ConvertToMarkdown(HtmlNode node)
    {
        var sb = new StringBuilder();
        ConvertNodeToMarkdown(node, sb, 0);
        // Collapse multiple blank lines into single
        var result = Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n");
        return result.Trim();
    }

    private static void ConvertNodeToMarkdown(HtmlNode node, StringBuilder sb, int depth)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = WebUtility.HtmlDecode(node.InnerText);
            if (!string.IsNullOrWhiteSpace(text))
                sb.Append(text.Trim());
            return;
        }

        if (node.NodeType != HtmlNodeType.Element)
            return;

        var tag = node.Name.ToLowerInvariant();

        switch (tag)
        {
            case "h1": sb.Append("\n\n# "); AppendChildren(node, sb, depth); sb.Append("\n"); break;
            case "h2": sb.Append("\n\n## "); AppendChildren(node, sb, depth); sb.Append("\n"); break;
            case "h3": sb.Append("\n\n### "); AppendChildren(node, sb, depth); sb.Append("\n"); break;
            case "h4": sb.Append("\n\n#### "); AppendChildren(node, sb, depth); sb.Append("\n"); break;
            case "h5": sb.Append("\n\n##### "); AppendChildren(node, sb, depth); sb.Append("\n"); break;
            case "h6": sb.Append("\n\n###### "); AppendChildren(node, sb, depth); sb.Append("\n"); break;

            case "p":
                sb.Append("\n\n");
                AppendChildren(node, sb, depth);
                sb.Append("\n\n");
                break;

            case "br":
                sb.Append("  \n");
                break;

            case "strong":
            case "b":
                sb.Append("**");
                AppendChildren(node, sb, depth);
                sb.Append("**");
                break;

            case "em":
            case "i":
                sb.Append("*");
                AppendChildren(node, sb, depth);
                sb.Append("*");
                break;

            case "code":
                sb.Append("`");
                AppendChildren(node, sb, depth);
                sb.Append("`");
                break;

            case "pre":
                sb.Append("\n\n```\n");
                sb.Append(WebUtility.HtmlDecode(node.InnerText).Trim());
                sb.Append("\n```\n\n");
                break;

            case "a":
                var href = node.GetAttributeValue("href", "");
                var linkText = WebUtility.HtmlDecode(node.InnerText).Trim();
                if (!string.IsNullOrEmpty(href) && !string.IsNullOrEmpty(linkText))
                    sb.Append($"[{linkText}]({href})");
                else
                    AppendChildren(node, sb, depth);
                break;

            case "img":
                var alt = node.GetAttributeValue("alt", "");
                var src = node.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(alt))
                    sb.Append($"![{alt}]({src})");
                break;

            case "ul":
            case "ol":
                sb.Append("\n");
                var isOrdered = tag == "ol";
                var itemIndex = 1;
                foreach (var child in node.ChildNodes)
                {
                    if (child.Name.ToLowerInvariant() == "li")
                    {
                        sb.Append(isOrdered ? $"{itemIndex}. " : "- ");
                        var liText = WebUtility.HtmlDecode(child.InnerText).Trim();
                        // Replace inner newlines with spaces
                        liText = Regex.Replace(liText, @"\s+", " ");
                        sb.AppendLine(liText);
                        itemIndex++;
                    }
                }
                sb.Append("\n");
                break;

            case "li":
                // Handled by ul/ol
                AppendChildren(node, sb, depth);
                break;

            case "table":
                ConvertTableToMarkdown(node, sb);
                break;

            case "thead":
            case "tbody":
            case "tfoot":
            case "tr":
            case "th":
            case "td":
                // Handled by table converter
                AppendChildren(node, sb, depth);
                break;

            case "blockquote":
                sb.Append("\n\n");
                var bqText = WebUtility.HtmlDecode(node.InnerText).Trim();
                foreach (var line in bqText.Split('\n'))
                    sb.AppendLine($"> {line.Trim()}");
                sb.Append("\n");
                break;

            case "hr":
                sb.Append("\n\n---\n\n");
                break;

            case "div":
            case "section":
            case "article":
            case "main":
            case "span":
            case "figure":
            case "figcaption":
            default:
                AppendChildren(node, sb, depth);
                break;
        }
    }

    private static void AppendChildren(HtmlNode node, StringBuilder sb, int depth)
    {
        foreach (var child in node.ChildNodes)
            ConvertNodeToMarkdown(child, sb, depth + 1);
    }

    private static void ConvertTableToMarkdown(HtmlNode table, StringBuilder sb)
    {
        sb.Append("\n\n");
        var rows = table.SelectNodes(".//tr");
        if (rows == null || rows.Count == 0) return;

        var headerRow = rows[0];
        var headers = headerRow.SelectNodes(".//th|.//td");
        if (headers == null) return;

        // Header row
        sb.Append("| ");
        sb.Append(string.Join(" | ", headers.Select(h => WebUtility.HtmlDecode(h.InnerText).Trim())));
        sb.Append(" |\n");

        // Separator
        sb.Append("| ");
        sb.Append(string.Join(" | ", Enumerable.Repeat("---", headers.Count)));
        sb.Append(" |\n");

        // Data rows
        foreach (var row in rows.Skip(1))
        {
            var cells = row.SelectNodes(".//th|.//td");
            if (cells == null) continue;
            sb.Append("| ");
            sb.Append(string.Join(" | ", cells.Select(c => WebUtility.HtmlDecode(c.InnerText).Trim())));
            sb.Append(" |\n");
        }

        sb.Append("\n");
    }
}
```

---

## Step 3: Register WebFetchClient in Program.cs

File: `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs`

Find the line:
```csharp
builder.Services.AddSingleton<BraveSearchClient>();
```

After that line, add:
```csharp
builder.Services.AddSingleton<IWebFetchClient, WebFetchClient>();
```

The `IHttpClientFactory` is already registered via `builder.Services.AddHttpClient()` at line 105 — no change needed there.

---

## Step 4: Add /internal/mcp/webfetch endpoint in Program.cs

File: `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs`

Find the closing block of the Brave endpoint (look for this exact text):
```csharp
    catch (Exception)
    {
        return Results.Problem("Brave search failed", statusCode: 500);
    }
}).AllowAnonymous().DisableAntiforgery();
```

After that block (after the `}).AllowAnonymous().DisableAntiforgery();` that closes the Brave endpoint), add the new web_fetch endpoint:

```csharp

// Internal MCP endpoint for Web Fetch — token-authenticated (used by harness)
app.MapPost("/internal/mcp/webfetch", async (HttpContext context, IWebFetchClient webFetchClient, IConfiguration config) =>
{
    if (!IsInternalAuthorized(context, config)) return Results.Unauthorized();

    using var reader = new StreamReader(context.Request.Body);
    var raw = await reader.ReadToEndAsync();

    JsonElement root;
    try
    {
        using var doc = JsonDocument.Parse(raw);
        root = doc.RootElement.Clone();
    }
    catch (JsonException)
    {
        return Results.BadRequest("Invalid JSON body");
    }

    // MCP JSON-RPC envelope: { method: "tools/call", params: { name, arguments } }
    var methodProp = root.TryGetProperty("method", out var m) ? m.GetString() : null;
    if (methodProp != "tools/call") return Results.BadRequest("Only tools/call supported");

    if (!root.TryGetProperty("params", out var paramsEl))
        return Results.BadRequest("Missing 'params'");
    if (!paramsEl.TryGetProperty("name", out var nameProp))
        return Results.BadRequest("Missing 'params.name'");
    var toolName = nameProp.GetString();
    if (toolName != "web_fetch") return Results.BadRequest($"Unknown tool: {toolName}");

    if (!paramsEl.TryGetProperty("arguments", out var args))
        return Results.BadRequest("Missing 'params.arguments'");
    var url = args.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
    if (string.IsNullOrWhiteSpace(url))
        return Results.BadRequest("Missing 'arguments.url'");

    var result = await webFetchClient.FetchAsync(url);

    string text;
    if (!result.Success)
    {
        text = $"Error fetching {url}: {result.ErrorMessage}";
    }
    else
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(result.Title))
            sb.AppendLine($"# {result.Title}");
        sb.AppendLine($"URL: {url}");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(result.MarkdownContent))
            sb.AppendLine(result.MarkdownContent);
        if (result.IsJsRendered)
            sb.AppendLine("\nNote: This page may use JavaScript rendering — some content may not be captured.");
        text = sb.ToString();
    }

    return Results.Ok(new { content = new[] { new { type = "text", text } } });
}).AllowAnonymous().DisableAntiforgery();
```

---

## Step 5: Seed web_fetch tool row in DatabaseInitializationService

File: `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/DatabaseInitializationService.cs`

This must use a NEW migration guard key: `"mcp-server-seed-v2"` — separate from `"mcp-server-seed-v1"` to avoid conflicts.

Find the block that starts with:
```csharp
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Microsoft 365 seed (non-fatal): {Message}", ex.Message);
            }

                    // Record migration as applied
                    using (var cmd = migConn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT IGNORE INTO applied_migrations (name, applied_at) VALUES (@name, NOW())";
                        var p = cmd.CreateParameter(); p.ParameterName = "@name"; p.Value = "mcp-server-seed-v1";
                        cmd.Parameters.Add(p);
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    _logger.LogInformation("MCP server seed migration complete (mcp-server-seed-v1).");
                }
                else
                {
                    _logger.LogInformation("MCP server seed already applied — skipping (mcp-server-seed-v1)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MCP server seed migration failed (non-fatal)");
            }
```

AFTER that entire block (after the closing `}` of the outer try/catch for mcp-server-seed-v1), add the following new migration block:

```csharp
            // Seed web_fetch MCP server — runs once, guarded by applied_migrations (mcp-server-seed-v2)
            try
            {
                var migConn2 = db.Database.GetDbConnection();
                if (migConn2.State != System.Data.ConnectionState.Open)
                    await migConn2.OpenAsync(cancellationToken);
                int webFetchSeedRan;
                using (var cmd = migConn2.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM applied_migrations WHERE name = @name";
                    var p = cmd.CreateParameter(); p.ParameterName = "@name"; p.Value = "mcp-server-seed-v2";
                    cmd.Parameters.Add(p);
                    webFetchSeedRan = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                }
                if (webFetchSeedRan == 0)
                {
                    try
                    {
                        var webFetchId = "00000000-0000-0000-0000-000000000004";
                        var webFetchEndpointUrl = "http://localhost:8080/internal/mcp/webfetch";
                        var webFetchManifest = System.Text.Json.JsonSerializer.Serialize(new[]
                        {
                            new
                            {
                                Name = "web_fetch",
                                Description = "Fetch and read the full content of a specific web page. Use when the user provides a URL and wants you to read or extract information from it. Returns clean markdown of the page content. Not for general search — use web_search for discovery.",
                                InputSchema = System.Text.Json.JsonDocument.Parse(@"{
                                  ""type"": ""object"",
                                  ""properties"": {
                                    ""url"": { ""type"": ""string"", ""description"": ""The full URL to fetch, including https://"" }
                                  },
                                  ""required"": [""url""]
                                }").RootElement
                            }
                        });
                        await db.Database.ExecuteSqlRawAsync(
                            """
                            INSERT INTO mcp_servers (id, name, slug, description, transport_type, endpoint_url,
                                auth_type, requires_user_auth, is_active, tool_manifest, created_at, updated_at)
                            VALUES ({0}, 'Web Fetch', 'webfetch', 'Fetch and read the full content of a specific web page',
                                'http', {1}, 'api_key', 0, 1, {2},
                                NOW(6), NOW(6))
                            ON DUPLICATE KEY UPDATE
                                endpoint_url = VALUES(endpoint_url),
                                updated_at = NOW(6)
                            """,
                            webFetchId, webFetchEndpointUrl, webFetchManifest);
                        _logger.LogInformation("Seeded Web Fetch MCP server (endpoint: {Url}).", webFetchEndpointUrl);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Web Fetch seed (non-fatal): {Message}", ex.Message);
                    }

                    // Record migration as applied
                    using (var cmd = migConn2.CreateCommand())
                    {
                        cmd.CommandText = "INSERT IGNORE INTO applied_migrations (name, applied_at) VALUES (@name, NOW())";
                        var p = cmd.CreateParameter(); p.ParameterName = "@name"; p.Value = "mcp-server-seed-v2";
                        cmd.Parameters.Add(p);
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    _logger.LogInformation("MCP server seed migration complete (mcp-server-seed-v2).");
                }
                else
                {
                    _logger.LogInformation("MCP server seed (v2) already applied — skipping.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Web Fetch MCP server seed migration failed (non-fatal)");
            }
```

---

## Step 6: Update harness-server.js — MCP_TOOL_SPECS + MCP_TOOL_ALLOWLIST + buildToolManifestSection + web_fetch handler + tool dispatch

File: `/home/fredw/projects/fip/fait/agent-harness/harness-server.js`

### 6a. Add web_fetch to MCP_TOOL_ALLOWLIST

Find:
```javascript
    'brave': new Set(['web_search']),
};
```

Replace with:
```javascript
    'brave': new Set(['web_search']),
    'webfetch': new Set(['web_fetch']),
};
```

### 6b. Add web_fetch to MCP_TOOL_SPECS

Find the `brave:` section:
```javascript
  brave: [
    {
      toolSpec: {
        name: 'web_search',
        description: 'Search the web using Brave Search. Use this when the user asks about current events, recent news, facts, or anything requiring up-to-date information from the internet.',
        inputSchema: {
          json: {
            type: 'object',
            properties: {
              query: { type: 'string', description: 'The search query' },
              count: { type: 'number', description: 'Number of results to return (1-10, default 5)' }
            },
            required: ['query']
          }
        }
      }
    }
  ]
};
```

Replace with:
```javascript
  brave: [
    {
      toolSpec: {
        name: 'web_search',
        description: 'Search the web using Brave Search. Use this when the user asks about current events, recent news, facts, or anything requiring up-to-date information from the internet.',
        inputSchema: {
          json: {
            type: 'object',
            properties: {
              query: { type: 'string', description: 'The search query' },
              count: { type: 'number', description: 'Number of results to return (1-10, default 5)' }
            },
            required: ['query']
          }
        }
      }
    }
  ],
  webfetch: [
    {
      toolSpec: {
        name: 'web_fetch',
        description: 'Fetch and read the full content of a specific web page. Use when the user provides a URL and wants you to read or extract information from it. Returns clean markdown of the page content. Not for general search — use web_search for discovery.',
        inputSchema: {
          json: {
            type: 'object',
            properties: {
              url: { type: 'string', description: 'The full URL to fetch, including https://' }
            },
            required: ['url']
          }
        }
      }
    }
  ]
};
```

### 6c. Add web_fetch to buildToolManifestSection

Find:
```javascript
        if (enabledPlugins.includes('brave')) {
            tools.push({ name: 'web_search', use: 'Searching the internet for current information' });
        }
```

Replace with:
```javascript
        if (enabledPlugins.includes('brave')) {
            tools.push({ name: 'web_search', use: 'Searching the internet for current information' });
        }
        if (enabledPlugins.includes('webfetch')) {
            tools.push({ name: 'web_fetch', use: 'Fetching and reading the full content of a specific URL' });
        }
```

### 6d. Add Web Tools guidance section to buildToolManifestSection return value

Find the function `buildToolManifestSection` and its return statement:
```javascript
    const rows = tools.map(t => `| ${t.name} | ${t.use} |`).join('\n');
    return `## Available Tools\n\n| Tool | Use when |\n|------|----------|\n${rows}`;
}
```

Replace with:
```javascript
    const rows = tools.map(t => `| ${t.name} | ${t.use} |`).join('\n');
    let section = `## Available Tools\n\n| Tool | Use when |\n|------|----------|\n${rows}`;

    // Add Web Tools guidance when both brave and webfetch are enabled
    if (Array.isArray(enabledPlugins) && enabledPlugins.includes('brave') && enabledPlugins.includes('webfetch')) {
        section += `\n\n## Web Tools\n\n**web_search** — Use for discovery: finding pages, researching topics, answering questions about what exists on the web. Returns a list of relevant URLs and summaries. Use when the user asks a general question that benefits from current web information.\n\n**web_fetch** — Use for extraction: reading the actual content of a specific page the user has provided or that you found via web_search. Returns the full page text as markdown. Use when:\n- The user provides a URL and asks you to read, summarize, or extract information from it\n- The user asks you to "match the style of" or "follow the format of" a specific website\n- You've found a promising result via web_search and need to read the full content\n- The user asks for specific details that wouldn't be in a search snippet\n\nDo not use web_search when the user has already given you a specific URL — use web_fetch directly.\nDo not use web_fetch for general questions where you don't have a target URL — use web_search first.`;
    }

    return section;
}
```

### 6e. Add /tools/web_fetch handler

Find the existing web_search handler:
```javascript
// ─── Brave web_search tool handler (ADO#3240) ─────────────────────────────
app.post('/tools/web_search', async (req, res) => {
```

After the entire web_search handler block (find where it ends — the last `});` before the `// ─── Write tool classification ─────────────────────────────────────────────` comment), add a new handler:

```javascript

// ─── Web Fetch tool handler (ADO#4559) ────────────────────────────────────
app.post('/tools/web_fetch', async (req, res) => {
    const { url } = req.body || {};
    if (!url) return res.status(400).json({ error: 'url required' });

    const webFetchUrl = `${FAIT_BASE_URL}/internal/mcp/webfetch`;
    const internalToken = INTERNAL_API_TOKEN;

    try {
        const resp = await fetch(webFetchUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
            },
            body: JSON.stringify({
                jsonrpc: '2.0',
                id: '1',
                method: 'tools/call',
                params: {
                    name: 'web_fetch',
                    arguments: { url }
                }
            }),
        });
        if (!resp.ok) {
            const text = await resp.text();
            throw new Error(`Web fetch MCP call failed (${resp.status}): ${text}`);
        }
        const mcpResponse = await resp.json();
        const content = mcpResponse?.result?.content;
        const text = Array.isArray(content) ? content.map(c => c.text || '').join('\n') : JSON.stringify(mcpResponse);
        if (mcpResponse?.result?.isError) {
            throw new Error(`Web fetch error: ${text}`);
        }
        res.json({ result: text });
    } catch (err) {
        console.error('[harness] web_fetch error:', err.message);
        res.status(500).json({ error: err.message });
    }
});
```

### 6f. Add web_fetch tool dispatch in the agentic turn loop

Find the existing web_search dispatch block (search for the exact block):
```javascript
                        } else if (toolUseAccumulator.name === 'web_search') {
                            emitToolCall(res, 'brave', 'web_search', 'calling', toolInput.query ? `Searching: ${chipTrunc(toolInput.query, 50)}` : 'Searching...');
                            try {
                                const mcpRes = await fetch(`http://localhost:${PORT}/tools/web_search`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, ...toolInput })
                                });
                                const mcpData = await mcpRes.json();
                                toolResultText = JSON.stringify(mcpData, null, 2);
                                emitToolCall(res, 'brave', 'web_search', 'done', 'Web search complete.');
                            } catch (mcpErr) {
                                toolResultText = `Web search error: ${mcpErr.message}`;
                                isError = true;
                                emitToolCall(res, 'brave', 'web_search', 'error', `Error: ${mcpErr.message.substring(0, 100)}`);
                            }
                        } else {
```

Replace with:
```javascript
                        } else if (toolUseAccumulator.name === 'web_search') {
                            emitToolCall(res, 'brave', 'web_search', 'calling', toolInput.query ? `Searching: ${chipTrunc(toolInput.query, 50)}` : 'Searching...');
                            try {
                                const mcpRes = await fetch(`http://localhost:${PORT}/tools/web_search`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, ...toolInput })
                                });
                                const mcpData = await mcpRes.json();
                                toolResultText = JSON.stringify(mcpData, null, 2);
                                emitToolCall(res, 'brave', 'web_search', 'done', 'Web search complete.');
                            } catch (mcpErr) {
                                toolResultText = `Web search error: ${mcpErr.message}`;
                                isError = true;
                                emitToolCall(res, 'brave', 'web_search', 'error', `Error: ${mcpErr.message.substring(0, 100)}`);
                            }
                        } else if (toolUseAccumulator.name === 'web_fetch') {
                            emitToolCall(res, 'webfetch', 'web_fetch', 'calling', toolInput.url ? `Fetching: ${chipTrunc(toolInput.url, 60)}` : 'Fetching page...');
                            try {
                                const mcpRes = await fetch(`http://localhost:${PORT}/tools/web_fetch`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, ...toolInput })
                                });
                                const mcpData = await mcpRes.json();
                                toolResultText = JSON.stringify(mcpData, null, 2);
                                emitToolCall(res, 'webfetch', 'web_fetch', 'done', 'Page fetched.');
                            } catch (mcpErr) {
                                toolResultText = `Web fetch error: ${mcpErr.message}`;
                                isError = true;
                                emitToolCall(res, 'webfetch', 'web_fetch', 'error', `Error: ${mcpErr.message.substring(0, 100)}`);
                            }
                        } else {
```

---

## Step 7: Update CLAUDE.md

File: `/home/fredw/projects/fip/fait/agent-harness/CLAUDE.md`

Find the end of the file (after the `## Workspace Awareness` section) and append:

```markdown

## Web Tools

**web_search** — Use for discovery: finding pages, researching topics, answering questions about what exists on the web. Returns a list of relevant URLs and summaries. Use when the user asks a general question that benefits from current web information.

**web_fetch** — Use for extraction: reading the actual content of a specific page the user has provided or that you found via web_search. Returns the full page text as markdown. Use when:
- The user provides a URL and asks you to read, summarize, or extract information from it
- The user asks you to "match the style of" or "follow the format of" a specific website
- You've found a promising result via web_search and need to read the full content
- The user asks for specific details that wouldn't be in a search snippet

Do not use web_search when the user has already given you a specific URL — use web_fetch directly.
Do not use web_fetch for general questions where you don't have a target URL — use web_search first.
```

---

## Step 8: Build Verification

After making all changes, run:
```bash
cd /home/fredw/projects/fip/fait/src/FortressAI.Web && dotnet restore && dotnet build --no-incremental
```

The build must succeed with no errors. Fix any compilation errors before completing.

Report back:
- Which files were created/modified
- The result of `dotnet build` (success/failure, any warnings)
- Confirmation that BraveSearchClient.cs was NOT modified
