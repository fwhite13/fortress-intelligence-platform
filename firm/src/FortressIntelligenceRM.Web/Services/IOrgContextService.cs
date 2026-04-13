namespace FortressIntelligenceRM.Web.Services;

public interface IOrgContextService
{
    Task<OrgContextDto?> GetContextAsync(string tenantId);
    Task UpsertContextAsync(string tenantId, string content, string updatedBy);
}

public class OrgContextDto
{
    public string? WikiContent { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
