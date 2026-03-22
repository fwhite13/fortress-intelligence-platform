namespace FamOs.Web.Data.Dtos;

public class CoverageChangeDto
{
    public Guid LineOfBusinessId { get; set; }
    public string FieldKey { get; set; } = "";
    public string FieldLabel { get; set; } = "";
    public string IncumbentValue { get; set; } = "";
    public string? ProposedValue { get; set; }
    public string ChangeType { get; set; } = "";   // "added", "removed", "reduced"
}
