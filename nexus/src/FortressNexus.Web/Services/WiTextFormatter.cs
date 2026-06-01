using System.Text;
using System.Text.RegularExpressions;

namespace FortressNexus.Web.Services;

internal static class WiTextFormatter
{
    private static readonly Regex AcCheckboxPattern = new(
        @"^\s*-\s*\[.\]\s*(.+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex AcNumberedPattern = new(
        @"^\s*\d+\.\s+(.+)", RegexOptions.Multiline | RegexOptions.Compiled);

    internal static string FormatDescriptionAsHtml(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        if (description.TrimStart().StartsWith('<'))
            return description;

        var paragraphs = description
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);

        return string.Concat(paragraphs.Select(p => $"<p>{p}</p>"));
    }

    internal static string FormatAcAsHtml(string? acceptanceCriteria)
    {
        if (string.IsNullOrWhiteSpace(acceptanceCriteria))
            return string.Empty;

        var items = ParseAcItems(acceptanceCriteria);
        if (items.Count == 0)
            return $"<p>{acceptanceCriteria}</p>";

        var sb = new StringBuilder("<ol>");
        foreach (var item in items)
            sb.Append($"<li>{item}</li>");
        sb.Append("</ol>");
        return sb.ToString();
    }

    private static List<string> ParseAcItems(string acceptanceCriteria)
    {
        var checkboxMatches = AcCheckboxPattern.Matches(acceptanceCriteria);
        if (checkboxMatches.Count > 0)
            return checkboxMatches.Select(m => m.Groups[1].Value.Trim()).Where(s => s.Length > 0).ToList();

        var numberedMatches = AcNumberedPattern.Matches(acceptanceCriteria);
        if (numberedMatches.Count > 0)
            return numberedMatches.Select(m => m.Groups[1].Value.Trim()).Where(s => s.Length > 0).ToList();

        return acceptanceCriteria
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }
}
