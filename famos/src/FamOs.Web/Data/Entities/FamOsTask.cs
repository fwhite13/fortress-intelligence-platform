namespace FamOs.Web.Data.Entities;

public class FamOsTask
{
    public Guid     Id              { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId   { get; set; }
    public string   Title           { get; set; } = "";
    public string   Status          { get; set; } = "open"; // open | done | cancelled
    public string?  AssignedToUserId { get; set; }
    public DateTime? DueAt          { get; set; }
    public DateTime  CreatedAt      { get; set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt      { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity  { get; set; } = default!;
}
