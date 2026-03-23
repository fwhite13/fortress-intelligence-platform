namespace FamOs.Web.Domain;

/// <summary>
/// Minimal interface for upload lifecycle operations — used by QuoteScraperPanel.
/// Scoped to avoid a full ILifecycleCommandService refactor across 12 injection sites.
/// </summary>
public interface IUploadLifecycleService
{
    Task<Guid> CreateUploadSubmissionAsync(Guid opportunityId, string carrierName, string? coverageTypes, string actorUserId);
    Task PersistFortressRequestIdAsync(Guid submissionId, string fortressRequestId);
    Task SetSubmissionErrorAsync(Guid submissionId, string errorMessage);
    Task SaveScraperResultAndRecordQuoteAsync(Guid opportunityId, Guid submissionId, string resultJson, decimal? parsedPremium, string actorUserId);
    Task ResetSubmissionScraperAsync(Guid submissionId);
}
