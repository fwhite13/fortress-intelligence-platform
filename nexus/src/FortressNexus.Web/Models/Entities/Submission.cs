using FortressNexus.Web.Models.Enums;

namespace FortressNexus.Web.Models.Entities;

public class Submission
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? FeatureArea { get; set; }
    public string NarrativeText { get; set; } = "";
    public int? MockupFileId { get; set; }
    public string SubmittedBy { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Draft;
    public int? ActiveSpecDocumentId { get; set; }

    // Navigation
    public UploadedFile? MockupFile { get; set; }
    public List<SpecDocument> SpecDocuments { get; set; } = new();
    public List<SubmissionFile> SubmissionFiles { get; set; } = new();
}
