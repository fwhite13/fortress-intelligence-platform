namespace FamOs.Web.Data.Dtos;

public class GapEvaluationResult
{
    public Dictionary<string, GapStatus> RequirementStatus { get; set; } = new();
    public Dictionary<string, int> PackageAGapsByLine { get; set; } = new();
    public Dictionary<string, int> PackageBGapsByLine { get; set; } = new();
    public List<string> UnsatisfiableRequirements { get; set; } = new();
}

public enum GapStatus { Unchecked, Covered, Gap, Unsatisfiable }
