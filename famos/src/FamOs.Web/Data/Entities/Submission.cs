namespace FamOs.Web.Data.Entities;

public class Submission
{
    public Guid     Id                { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId     { get; set; }
    public string   CarrierName       { get; set; } = "";
    public string   Status            { get; set; } = "pending"; // pending | sent | responded | declined
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt           { get; set; }
    public DateTime? RespondedAt      { get; set; }

    public Opportunity Opportunity    { get; set; } = default!;
    public List<Quote> Quotes         { get; set; } = new();
}
