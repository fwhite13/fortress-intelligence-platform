namespace FamOs.Web.Data.Entities;

public class IntakeResponse
{
    public long Id { get; set; }
    public string OpportunityId { get; set; } = "";
    public string FieldCode { get; set; } = "";
    public string? Value { get; set; }
    public string? PageName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
