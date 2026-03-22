namespace FamOs.Web.Data.Dtos;

public class IncumbentUpsertDto
{
    public Guid AccountId { get; set; }
    public Guid LineOfBusinessId { get; set; }
    public int TenantId { get; set; }
    public string CarrierName { get; set; } = "";
    public string? PolicyNumber { get; set; }
    public decimal AnnualPremium { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public Dictionary<string, string> Vals { get; set; } = new();
    public string SourceType { get; set; } = "manual";
    public Guid? UserId { get; set; }
}
