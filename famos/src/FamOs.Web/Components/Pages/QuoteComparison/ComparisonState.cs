using FamOs.Web.Data.Dtos;

namespace FamOs.Web.Components.Pages.QuoteComparison;

/// <summary>
/// Client-side UI state for the Quote Comparison page.
/// State mutations ONLY go through QuoteComparisonPage methods
/// to ensure alert re-evaluation fires on every change.
/// </summary>
public class ComparisonPageState
{
    public HashSet<string> CheckedRequirements { get; set; } = new();
    public Dictionary<string, Guid> PackageASelections { get; set; } = new();
    public Dictionary<string, Guid> PackageBSelections { get; set; } = new();
    public HashSet<string> AutoBundledA { get; set; } = new();
    public HashSet<string> AutoBundledB { get; set; } = new();
    public string ActivePackage { get; set; } = "A";
    public bool ShowIncumbent { get; set; } = false;
    public HashSet<string> CollapsedBlocks { get; set; } = new();
    public bool ShowCompareView { get; set; } = false;
    public List<AlertDto> ActiveAlerts { get; set; } = new();
}
