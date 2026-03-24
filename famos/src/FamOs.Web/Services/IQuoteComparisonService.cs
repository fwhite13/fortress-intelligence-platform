using FamOs.Web.Data.Dtos;

namespace FamOs.Web.Services;

public interface IQuoteComparisonService
{
    Task<ComparisonContextDto> GetComparisonContextAsync(Guid accountId, Guid opportunityId, Guid userId, int tenantId);
    Task<List<QuoteWithCoverageDto>> GetQuotesForAccountAsync(Guid accountId, int tenantId);
    Task SaveDraftAsync(Guid accountId, Guid userId, int tenantId, DraftStateDto draft);
    Task<Guid> BuildProposalAsync(Guid accountId, Guid opportunityId, Guid userId, Guid packageId, int tenantId);
    Task<Guid?> ResolveAccountIdFromOpportunityAsync(Guid opportunityId, int tenantId);
}
