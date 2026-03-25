namespace FamOs.Web.Components.Pages.IntakeQuestionnaire;

public class ConditionEvaluator
{
    private Dictionary<string, string> _responses = new();

    public void UpdateResponse(string fieldCode, string value)
        => _responses[fieldCode] = value;

    public void SetAll(Dictionary<string, string> responses)
        => _responses = new Dictionary<string, string>(responses);

    public bool Evaluate(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        // OR: split on " or " (case-insensitive), any true → visible
        var parts = condition.Split(new[] { " or " }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(EvaluateSingle);
    }

    private bool EvaluateSingle(string condition)
    {
        // Pattern: {field_code} = 'Value'
        var match = System.Text.RegularExpressions.Regex.Match(
            condition.Trim(),
            @"\{(\w+)\}\s*=\s*'([^']+)'");
        if (!match.Success) return true; // unknown syntax → show
        var field = match.Groups[1].Value;
        var expected = match.Groups[2].Value;
        _responses.TryGetValue(field, out var actual);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
