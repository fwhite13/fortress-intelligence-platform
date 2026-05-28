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
