namespace FamOs.Web.Data.Entities;

public class Quote
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid     SubmissionId        { get; set; }
    public string   CarrierName         { get; set; } = "";
    public decimal  PremiumAmount       { get; set; }
    public string?  CoverageDetails     { get; set; }   // JSON string
    public bool     IsRecommended       { get; set; } = false;
    public DateTime ReceivedAt          { get; set; } = DateTime.UtcNow;
    public Guid?    LineOfBusinessId    { get; set; }
    public int      TenantId            { get; set; }

    public Opportunity Opportunity      { get; set; } = default!;
    public Submission  Submission       { get; set; } = default!;
}
