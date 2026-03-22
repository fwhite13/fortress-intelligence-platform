namespace FamOs.Web.Data.Entities;

public class Package
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public int TenantId { get; set; }
    public string Label { get; set; } = "A";         // "A" or "B"
    public string Status { get; set; } = "draft";    // "draft", "submitted", "archived"
    public decimal TotalPremium { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? LastModifiedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
