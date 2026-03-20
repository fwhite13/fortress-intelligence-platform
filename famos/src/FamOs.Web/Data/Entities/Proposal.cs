namespace FamOs.Web.Data.Entities;

public class Proposal
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid     RecommendedQuoteId  { get; set; }
    public int      Version             { get; set; } = 1;

    /// <summary>"draft" | "sent" | "accepted" | "declined"</summary>
    public string   Status              { get; set; } = "draft";

    // Sprint 7 additions
    public string?  CarrierName         { get; set; }
    public string?  CoverageTypes       { get; set; }
    public DateTime ProposalDate        { get; set; } = DateTime.UtcNow;
    public string?  Notes               { get; set; }

    public DateTime? SentAt             { get; set; }
    public DateTime? ClientDecisionAt   { get; set; }
    public string?   DeclineReason      { get; set; }

    // Audit
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity      { get; set; } = default!;
}
