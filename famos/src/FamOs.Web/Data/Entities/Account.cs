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
    public bool      IsRenewal     { get; set; } = false;
    public Guid?     ProgramVerticalId { get; set; }

    // ADO#1016 — HubSpot field mapping: store synced values directly
    public string?   AccountStatus   { get; set; }  // "Active" | "Prospect" | "Inactive"
    public string?   PrimaryCoverage { get; set; }  // from primary deal: coverage line/type
    public string?   PrimaryCarrier  { get; set; }  // from primary deal: carrier name
    public DateTime? PolicyExpiresAt { get; set; }  // from primary deal: expiration date
    public string?   PrimaryDealId   { get; set; }  // HubSpot deal ID for primary deal
}
