namespace FortressIntelligenceRM.Web.Services;

public class BrandingConfig
{
    public string SuiteName { get; set; } = "FIP";
    public string SuiteFullName { get; set; } = "Fortress Intelligence Platform";
    public string ModuleName { get; set; } = "FIRM";
    public string ModuleFullName { get; set; } = "Fortress Intelligence Recording & Minutes";
    public string OrgName { get; set; } = "Fortress";
    public string PortalUrl { get; set; } = "";
    public string LogoEmoji { get; set; } = "\ud83c\udff0";
    public string HomeTenantId { get; set; } = "7152ea12-c930-44b0-bb52-069152161c5b";
    public string PrimaryColor { get; set; } = "#1a2332";
    public string AccentColor { get; set; } = "#d4af37";
    public string SidebarBg { get; set; } = "#1A2035";
    public string HeaderBg { get; set; } = "#1E293B";

    public string NotetakerName => $"{OrgName} Notetaker";

    public bool IsHomeTenant(string? tenantId) =>
        !string.IsNullOrEmpty(tenantId) &&
        tenantId.StartsWith(HomeTenantId[..8], StringComparison.OrdinalIgnoreCase);
}
