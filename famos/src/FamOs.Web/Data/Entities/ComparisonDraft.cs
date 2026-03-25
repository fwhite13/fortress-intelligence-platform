namespace FamOs.Web.Data.Entities;

public class ComparisonDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AccountId { get; set; }
    public Guid? OpportunityId { get; set; }
    public int TenantId { get; set; }
    public Guid UserId { get; set; }
    public string? ActiveRequirementSlugs { get; set; }  // JSON array of checked requirement slugs
    public string? PackageASelections { get; set; }      // JSON: {lineSlug: quoteId}
    public string? PackageBSelections { get; set; }      // JSON: {lineSlug: quoteId}
    public bool ShowIncumbent { get; set; } = false;
    public string? CollapsedBlocks { get; set; }         // JSON array of collapsed LOB slugs
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}
