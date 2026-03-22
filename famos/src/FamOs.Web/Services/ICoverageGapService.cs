using FamOs.Web.Data.Dtos;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public interface ICoverageGapService
{
    GapEvaluationResult EvaluateGaps(
        HashSet<string> checkedRequirementSlugs,
        List<QuoteWithCoverageDto> allQuotes,
        Dictionary<string, Guid> packageASelections,
        Dictionary<string, Guid> packageBSelections,
        List<Requirement> requirements,
        List<LineOfBusiness> lines);

    List<CoverageChangeDto> DetectCoverageRemovals(
        IncumbentPolicyDto incumbent,
        QuoteWithCoverageDto proposedQuote,
        LineOfBusiness lob);
}
