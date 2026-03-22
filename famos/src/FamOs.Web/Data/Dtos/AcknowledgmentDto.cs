namespace FamOs.Web.Data.Dtos;

public class AcknowledgmentDto
{
    public Guid LineOfBusinessId { get; set; }
    public string FieldKey { get; set; } = "";
    public string ChangeType { get; set; } = "";
    public DateTime AcknowledgedAt { get; set; }
}
