namespace FamOs.Web.Data.Entities;

public class CoverageRemovalAcknowledgment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid PackageId { get; set; }
    public int TenantId { get; set; }
    public Guid AcknowledgedByUserId { get; set; }
    public DateTime AcknowledgedAt { get; set; } = DateTime.UtcNow;
    public string CoverageDescription { get; set; } = "";
    public Guid LineOfBusinessId { get; set; }
    public string IncumbentFieldKey { get; set; } = "";
    public string IncumbentValue { get; set; } = "";
    public string? ProposedValue { get; set; }
    public string ChangeType { get; set; } = "removed";  // "removed" or "reduced"
}
