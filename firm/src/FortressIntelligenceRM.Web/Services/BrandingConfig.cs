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

    public string? NotetakerNameOverride { get; set; }
    public string NotetakerName => NotetakerNameOverride ?? $"{OrgName} Notetaker";

    /// <summary>WI #9680: true when ModuleName already names the org (RN: "Refuge Notetaker" starts with
    /// "Refuge"), so UI that would print OrgName next to ModuleName prints ModuleName alone.</summary>
    public bool ModuleNameIncludesOrg =>
        ModuleName.StartsWith(OrgName ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>WI #9680: "{OrgName} {ModuleName}" ("Fortress FIRM"), or ModuleName alone when it already
    /// starts with OrgName ("Refuge Notetaker").</summary>
    public string OrgModuleName => ModuleNameIncludesOrg ? ModuleName : $"{OrgName} {ModuleName}";

    public bool IsHomeTenant(string? tenantId) =>
        !string.IsNullOrEmpty(tenantId) &&
        tenantId.StartsWith(HomeTenantId[..8], StringComparison.OrdinalIgnoreCase);
}
