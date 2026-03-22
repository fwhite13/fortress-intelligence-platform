namespace FamOs.Web.Data.Dtos;

public class PackageSaveDto
{
    public Guid? Id { get; set; }
    public string Label { get; set; } = "A";
    public List<PackageSelectionDto> Selections { get; set; } = new();
}
