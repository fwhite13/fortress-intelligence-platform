namespace FamOs.Web.Data.Dtos;

public class DraftStateDto
{
    public HashSet<string> CheckedRequirements { get; set; } = new();
    public Dictionary<string, Guid> PackageASelections { get; set; } = new();
    public Dictionary<string, Guid> PackageBSelections { get; set; } = new();
    public bool ShowIncumbent { get; set; }
    public HashSet<string> CollapsedBlocks { get; set; } = new();
}
