namespace FamOs.Web.Data.Entities;

public class ProgramVertical
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public string Name { get; set; } = "";           // "TIG Trucking", "IAAPA"
    public string Slug { get; set; } = "";           // "tig-trucking", "iaapa"
    public bool IsActive { get; set; } = true;
    public string? FaitPresetChips { get; set; }     // JSON array: [{label, prompt}]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
