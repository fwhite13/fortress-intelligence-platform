namespace FamOs.Web.Data.Entities;

public class PolicyShadowRecord
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid?    WinningQuoteId      { get; set; }

    // Carrier/policy identity
    public string?  CarrierName         { get; set; }
    public string?  PolicyNumber        { get; set; }    // Sprint 7
    public string?  CoverageType        { get; set; }    // Sprint 7

    // Dates
    public DateOnly? PolicyEffectiveDate { get; set; }
    public DateOnly? ExpirationDate      { get; set; }   // Sprint 7
    public DateOnly? RenewalTimerStart   { get; set; }
    public DateTime? BoundAt             { get; set; }   // Sprint 7

    // Financials
    public decimal?  PremiumAmount      { get; set; }

    // Coverage snapshot
    public string?   SnapshotJson       { get; set; }

    // Audit
    public DateTime  CreatedAt          { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity      { get; set; } = default!;
}
