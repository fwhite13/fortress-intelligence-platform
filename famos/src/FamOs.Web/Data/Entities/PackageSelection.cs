namespace FamOs.Web.Data.Entities;

public class PackageSelection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public Guid LineOfBusinessId { get; set; }
    public Guid QuoteId { get; set; }
    public bool IsAutoBundle { get; set; } = false;  // true = added by bundle logic
    public int TenantId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
