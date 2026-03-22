namespace FamOs.Web.Data.Dtos;

public class PackageDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Label { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal TotalPremium { get; set; }
    public List<PackageSelectionDto> Selections { get; set; } = new();
}
