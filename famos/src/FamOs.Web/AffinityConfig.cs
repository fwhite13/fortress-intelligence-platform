namespace FamOs.Web;

public class AffinityConfig
{
    public string  AffinityId    { get; set; } = "famos";
    public string  DisplayName   { get; set; } = "Fortress Affinity Management OS";
    public string  PortalName    { get; set; } = "FAM OS";
    public string  LogoPath      { get; set; } = "";
    public string? PrimaryColor  { get; set; }
    public string? AccentColor   { get; set; }

    /// <summary>
    /// Known users for this affinity program.
    /// Populated via appsettings. Phase 1: manual list. Phase 2: pulled from identity provider.
    /// </summary>
    public List<AffinityUser> Users { get; set; } = new();
}

public class AffinityUser
{
    public string UserId      { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Initials    { get; set; } = "";
}
