using FamOs.Web.Domain;

namespace FamOs.Web.Data.Entities;

public class OpportunityFlag
{
    public Guid             Id              { get; set; } = Guid.NewGuid();
    public Guid             OpportunityId   { get; set; }
    public OpportunityFlagType FlagType     { get; set; }
    public string?          Reason          { get; set; }
    public DateTime         SetAt           { get; set; } = DateTime.UtcNow;
    public bool             IsActive        { get; set; } = true;

    public Opportunity      Opportunity     { get; set; } = default!;
}
