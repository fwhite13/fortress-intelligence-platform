namespace FamOs.Web.Data.Entities;

public class IncumbentPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid LineOfBusinessId { get; set; }
    public int TenantId { get; set; }
    public string CarrierName { get; set; } = "";
    public string? PolicyNumber { get; set; }
    public decimal AnnualPremium { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public string? Vals { get; set; }                // JSON: same key/value as CoverageDetails.Vals
    public string SourceType { get; set; } = "manual";  // "scraper" or "manual"
    public string? ScraperRunId { get; set; }
    public bool IsOverridden { get; set; } = false;
    public Guid? OverriddenByUserId { get; set; }
    public DateTime? OverriddenAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
