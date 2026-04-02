using FortressNexus.Web.Models;
using HtmlAgilityPack;

namespace FortressNexus.Web.Services;

public class MockupSectionizerService : IMockupSectionizer
{
    private readonly ILogger<MockupSectionizerService> _logger;

    public MockupSectionizerService(ILogger<MockupSectionizerService> logger)
    {
        _logger = logger;
    }

    public Task<List<MockupSection>> SectionizeAsync(string htmlContent, string submissionId)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        var sections = new List<MockupSection>();

        // Find structural elements
        var structuralXPath = "//section | //article | //main | //header | //footer | //div[@id or @class]";
        var nodes = doc.DocumentNode.SelectNodes(structuralXPath);

        if (nodes is not null)
        {
            foreach (var node in nodes)
            {
                var label = GetLabel(node);
                var htmlSnippet = node.OuterHtml;
                var textContent = GetCleanText(node);

                if (string.IsNullOrWhiteSpace(textContent)) continue;

                sections.Add(new MockupSection(label, htmlSnippet, null, textContent));
            }
        }

        // Fallback: fewer than 2 sections -> treat whole document as one section
        if (sections.Count < 2)
        {
            sections.Clear();
            var allText = GetCleanText(doc.DocumentNode);
            sections.Add(new MockupSection("Document", htmlContent, null, allText));
            _logger.LogInformation("NEXUS: Sectionizer fallback for submission {Id} — used full document", submissionId);
        }
        else
        {
            _logger.LogInformation("NEXUS: Sectionizer found {Count} sections for submission {Id}", sections.Count, submissionId);
        }

        return Task.FromResult(sections);
    }

    private static string GetLabel(HtmlNode node)
    {
        var id = node.GetAttributeValue("id", "");
        if (!string.IsNullOrWhiteSpace(id)) return id;

        var cls = node.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!string.IsNullOrWhiteSpace(cls)) return cls;

        return node.Name;
    }

    private static string GetCleanText(HtmlNode node)
    {
        foreach (var script in node.SelectNodes(".//script|.//style") ?? Enumerable.Empty<HtmlNode>())
            script.Remove();
        return HtmlEntity.DeEntitize(node.InnerText).Trim();
    }
}
