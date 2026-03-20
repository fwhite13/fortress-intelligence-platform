namespace FamOs.Web;

public class AffinityConfig
{
    public string  AffinityId    { get; set; } = "famos";
    public string  DisplayName   { get; set; } = "Fortress Affinity Management OS";
    public string  PortalName    { get; set; } = "FAM OS";
    public string  LogoPath      { get; set; } = "";
    public string? PrimaryColor  { get; set; }
    public string? AccentColor   { get; set; }
    public List<AffinityUser> Users { get; set; } = new();

    /// <summary>
    /// All affinity groups served by this deployment.
    /// If empty, falls back to the single-affinity AffinityId/DisplayName/PortalName values.
    /// </summary>
    public List<AffinityGroupConfig> AffinityGroups { get; set; } = new();

    /// <summary>
    /// Maps Entra user email → affinity group ID.
    /// Used when affinityId Entra claim is not present.
    /// </summary>
    public Dictionary<string, string> UserAffinityMap { get; set; } = new();
}

public class AffinityUser
{
    public string UserId      { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Initials    { get; set; } = "";
}

public class AffinityGroupConfig
{
    public string  AffinityId   { get; set; } = "";
    public string  DisplayName  { get; set; } = "";
    public string  PortalName   { get; set; } = "";
    public string  LogoPath     { get; set; } = "";
    public string? PrimaryColor { get; set; }
}
