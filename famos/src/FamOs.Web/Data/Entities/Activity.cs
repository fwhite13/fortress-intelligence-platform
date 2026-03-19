namespace FamOs.Web.Data.Entities;

public class Activity
{
    public Guid     Id              { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId   { get; set; }
    public string   EventType       { get; set; } = "";
    public string?  Description     { get; set; }
    public string?  ActorUserId     { get; set; }
    public string?  MetadataJson    { get; set; }
    public DateTime OccurredAt      { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity  { get; set; } = default!;
}
