using FamOs.Web.Data.Dtos;

namespace FamOs.Web.Services;

public interface IAlertService
{
    List<AlertDto> EvaluateAlerts(
        ComparisonContextDto context,
        ComparisonState state,
        GapEvaluationResult gaps);
}

/// <summary>
/// Represents the current package selection state for alert evaluation.
/// Keys are line slugs; values are selected quote IDs.
/// </summary>
public class ComparisonState
{
    public Dictionary<string, Guid> PackageASelections { get; set; } = new();
    public Dictionary<string, Guid> PackageBSelections { get; set; } = new();
}
