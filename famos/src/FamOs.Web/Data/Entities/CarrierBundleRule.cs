namespace FamOs.Web.Data.Entities;

public class CarrierBundleRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public string CarrierName { get; set; } = "";        // must match carrier name in quote data
    public string PrimaryLineSlug { get; set; } = "";    // line that triggers bundle (e.g. "gl")
    public string RequiredLineSlug { get; set; } = "";   // line that must be bundled (e.g. "cargo")
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}
