namespace FortressIntelligenceRM.Web.Models;

/// <summary>
/// WI #9133 — a dynamically registered (RFC 7591) OAuth client, one per provider and redirect URI
/// (firm_oauth_clients, spec §9.2). Dev and prod share the DB, so each environment keeps its own row.
/// </summary>
public class FirmOAuthClient
{
    public string Provider { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string ClientId { get; set; } = "";
    public DateTime RegisteredAt { get; set; }
}
