namespace FamOs.Web.Data.Entities;

/// <summary>
/// Local cache of HubSpot company records for an affinity group.
/// Refreshed by AccountSyncService. Source of truth is HubSpot.
/// </summary>
public class Account
{
    public Guid    Id              { get; set; } = Guid.NewGuid();
    public string  AffinityId      { get; set; } = "";
    public string  CompanyName     { get; set; } = "";
    public string? HubSpotId       { get; set; }
    public string? City            { get; set; }
    public string? State           { get; set; }
    public int     ActiveOppCount  { get; set; } = 0;
    public DateTime? LastSyncedAt  { get; set; }
    public DateTime  CreatedAt     { get; set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt     { get; set; } = DateTime.UtcNow;
}
