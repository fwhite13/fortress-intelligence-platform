namespace FortressIntelligenceRM.Web.Services;

public record OrgContextEntry(string Term, string Description);

public interface IOrgContextService
{
    Task<List<OrgContextEntry>> GetContextAsync(string tenantId);
    Task<DateTime?> GetUpdatedAtAsync(string tenantId);
    Task<string?> GetUpdatedByAsync(string tenantId);
    Task UpsertContextAsync(string tenantId, List<OrgContextEntry> entries, string updatedBy);
}
