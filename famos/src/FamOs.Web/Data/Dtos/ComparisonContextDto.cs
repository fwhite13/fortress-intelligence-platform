using FamOs.Web.Data.Entities;

namespace FamOs.Web.Data.Dtos;

public class ComparisonContextDto
{
    public Account Account { get; set; } = default!;
    public ProgramVertical? ProgramVertical { get; set; }
    public List<LineOfBusiness> Lines { get; set; } = new();
    public List<Requirement> Requirements { get; set; } = new();
    public List<QuoteWithCoverageDto> Quotes { get; set; } = new();
    public Dictionary<Guid, IncumbentPolicyDto> Incumbents { get; set; } = new();
    public Dictionary<Guid, decimal> Benchmarks { get; set; } = new();  // LineOfBusinessId -> premium
    public List<CarrierBundleRule> BundleRules { get; set; } = new();
    public DraftStateDto? SavedDraft { get; set; }
}
