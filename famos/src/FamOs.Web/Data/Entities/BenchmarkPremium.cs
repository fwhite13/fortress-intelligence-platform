namespace FamOs.Web.Data.Entities;

public class BenchmarkPremium
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProgramVerticalId { get; set; }
    public Guid LineOfBusinessId { get; set; }
    public int TenantId { get; set; }
    public decimal AnnualPremium { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string Source { get; set; } = "manual";       // "manual", "internal-history", "third-party"
    public string? Notes { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
