using FamOs.Web.Data.Dtos;

namespace FamOs.Web.Services;

public interface IQuoteComparisonService
{
    Task<ComparisonContextDto> GetComparisonContextAsync(Guid opportunityId, Guid userId, int tenantId);
    Task<List<QuoteWithCoverageDto>> GetQuotesForAccountAsync(Guid accountId, int tenantId);
    Task SaveDraftAsync(Guid opportunityId, Guid userId, int tenantId, DraftStateDto draft);
    Task<Guid> BuildProposalAsync(Guid opportunityId, Guid userId, Guid packageId, int tenantId);
}
