namespace FamOs.Web.Data.Dtos;

public class IncumbentPolicyDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid LineOfBusinessId { get; set; }
    public string CarrierName { get; set; } = "";
    public string? PolicyNumber { get; set; }
    public decimal AnnualPremium { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public Dictionary<string, string> Vals { get; set; } = new();
    public string SourceType { get; set; } = "";
    public bool IsOverridden { get; set; }
}
