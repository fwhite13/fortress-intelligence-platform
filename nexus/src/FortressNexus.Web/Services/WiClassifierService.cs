using System.Text.RegularExpressions;
using FortressNexus.Web.Models.DTOs;

namespace FortressNexus.Web.Services;

public class WiClassifierService : IWiClassifier
{
    private static readonly string[] InfrastructureSignals =
    {
        "create ecr", "ecr repo", "iam role", "ecs service", "alb target",
        "alb rule", "secrets manager secret", "target group",
        "fargate task definition", "ecr repository", "task execution role"
    };

    private static readonly string[] MigrationSignals =
    {
        "migrate", "replace", "move from", "deprecate",
        "switch from", "transition from", "cut over"
    };

    private static readonly string[] AuthScopingSignals =
    {
        "auth", "token", "entitlement", "scope", "scoping", "permission",
        "validate", "enforce", "restrict", "deny", "unauthorized",
        "403", "jwt", "bearer"
    };

    private static readonly string[] ExternalDependencySignals =
    {
        "rob", "rob nethery", "cloudflare", "cf config", "cf route",
        "azure access", "iam request", "iam permissions",
        "secrets manager access", "ado pat", "pat token"
    };

    private static readonly Regex AcItemPattern = new(
        @"^(\s*- \[ \]|\s*\d+[\.\)])",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public WiTemplateType ClassifyStory(AdoWorkItemDto story)
    {
        var text = CombineTitleAndDescription(story);

        if (ContainsAny(text, InfrastructureSignals))
            return WiTemplateType.Infrastructure;

        if (ContainsAny(text, MigrationSignals))
            return WiTemplateType.Migration;

        return WiTemplateType.Standard;
    }

    public bool ShouldGenerateTestCases(AdoWorkItemDto story)
    {
        if (ClassifyStory(story) != WiTemplateType.Standard)
            return false;

        var text = CombineTitleAndDescription(story);

        if (ContainsAny(text, AuthScopingSignals))
            return true;

        var ac = story.AcceptanceCriteria ?? "";
        var distinctAcCount = AcItemPattern.Matches(ac).Count;
        return distinctAcCount >= 4;
    }

    public bool IsExternalDependency(AdoWorkItemDto wi)
    {
        // Epics and Features are structural containers — never external dependencies
        if (wi.WorkItemType is "Epic" or "Feature")
            return false;

        var text = CombineTitleAndDescription(wi);
        return ContainsAny(text, ExternalDependencySignals);
    }

    public string? ExtractExternalOwner(AdoWorkItemDto wi)
    {
        if (!IsExternalDependency(wi))
            return null;

        var text = CombineTitleAndDescription(wi);

        if (ContainsAny(text, new[] { "rob", "cloudflare", "cf config" }))
            return "Rob Nethery";

        if (ContainsAny(text, new[] { "iam", "bedrock-agent-runtime" }))
            return "AWS IAM";

        if (ContainsAny(text, new[] { "azure access", "azure subscription" }))
            return "Azure Admin";

        if (ContainsAny(text, new[] { "ado pat", "pat token" }))
            return "ADO Admin";

        return "External Owner";
    }

    private static string CombineTitleAndDescription(AdoWorkItemDto wi)
    {
        return $"{wi.Title}\n{wi.Description}".ToLowerInvariant();
    }

    private static bool ContainsAny(string text, string[] signals)
    {
        foreach (var signal in signals)
        {
            if (text.Contains(signal, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
