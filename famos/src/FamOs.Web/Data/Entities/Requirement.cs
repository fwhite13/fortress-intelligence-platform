namespace FamOs.Web.Data.Entities;

public class Requirement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProgramVerticalId { get; set; }
    public int TenantId { get; set; }
    public string Slug { get; set; } = "";           // "rq-gl-occ"
    public string Label { get; set; } = "";          // display text
    public string GroupName { get; set; } = "";      // "General Liability" for sidebar grouping
    public Guid LineOfBusinessId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
