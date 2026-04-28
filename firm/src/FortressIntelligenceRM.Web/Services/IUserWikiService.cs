namespace FortressIntelligenceRM.Web.Services;

public interface IUserWikiService
{
    Task<List<OrgContextEntry>> GetEntriesAsync(string entraOid, string tenantId);
    Task UpsertEntriesAsync(string entraOid, string tenantId, List<OrgContextEntry> entries, string updatedBy);
    Task<DateTime?> GetUpdatedAtAsync(string entraOid, string tenantId);
}
