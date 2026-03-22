namespace FamOs.Web.Data.Entities;

public class LineOfBusiness
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProgramVerticalId { get; set; }
    public int TenantId { get; set; }
    public string Slug { get; set; } = "";           // "gl", "auto", "cargo"
    public string Name { get; set; } = "";           // "General Liability"
    public string Icon { get; set; } = "";           // emoji: "🛡️"
    public string? MetaDescription { get; set; }     // short descriptor
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? FieldDefinitions { get; set; }    // JSON: [{key, label, order}]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
