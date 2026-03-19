namespace FamOs.Web.Data.Entities;

public class Proposal
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid     RecommendedQuoteId  { get; set; }
    public int      Version             { get; set; } = 1;
    public string   Status              { get; set; } = "draft"; // draft | sent | accepted | declined
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt             { get; set; }
    public DateTime? ClientDecisionAt   { get; set; }
    public string?  DeclineReason       { get; set; }

    public Opportunity Opportunity      { get; set; } = default!;
}
