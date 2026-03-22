namespace FamOs.Web.Data.Dtos;

public class PackageSelectionDto
{
    public Guid LineOfBusinessId { get; set; }
    public Guid QuoteId { get; set; }
    public bool IsAutoBundle { get; set; }
}
