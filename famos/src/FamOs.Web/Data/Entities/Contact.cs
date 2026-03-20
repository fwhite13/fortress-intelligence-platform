namespace FamOs.Web.Data.Entities;

public class Contact
{
    public Guid    Id             { get; set; } = Guid.NewGuid();
    public Guid    OpportunityId  { get; set; }
    public string  FirstName      { get; set; } = "";
    public string  LastName       { get; set; } = "";
    public string? Title          { get; set; }
    public string? Email          { get; set; }
    public string? Phone          { get; set; }
    public ContactType ContactType { get; set; } = ContactType.Primary;
    public string? Notes          { get; set; }
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt     { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity { get; set; } = default!;

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public enum ContactType
{
    Primary          = 0,
    Billing          = 1,
    DecisionMaker    = 2,
    TechnicalContact = 3
}
