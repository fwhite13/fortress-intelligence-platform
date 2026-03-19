namespace FamOs.Web.Data.Entities;

public class PolicyShadowRecord
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid?    WinningQuoteId      { get; set; }
    public string?  CarrierName         { get; set; }
    public DateOnly? PolicyEffectiveDate { get; set; }
    public decimal?  PremiumAmount      { get; set; }
    public DateOnly? RenewalTimerStart  { get; set; }
    public string?   SnapshotJson       { get; set; }  // coverage summary + carrier + pricing
    public DateTime  CreatedAt          { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity      { get; set; } = default!;
}
