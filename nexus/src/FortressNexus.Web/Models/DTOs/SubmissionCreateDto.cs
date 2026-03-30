namespace FortressNexus.Web.Models.DTOs;

public class SubmissionCreateDto
{
    public string Title { get; set; } = "";
    public string? FeatureArea { get; set; }
    public string NarrativeText { get; set; } = "";
    public int MockupFileId { get; set; }
}
