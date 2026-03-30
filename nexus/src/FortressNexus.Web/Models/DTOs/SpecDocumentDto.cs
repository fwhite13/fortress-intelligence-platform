namespace FortressNexus.Web.Models.DTOs;

public class SpecDocumentDto
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public int Version { get; set; }
    public string Content { get; set; } = "";
    public string? EditedContent { get; set; }
    public bool IsApproved { get; set; }
    public DateTime GeneratedAt { get; set; }
}
