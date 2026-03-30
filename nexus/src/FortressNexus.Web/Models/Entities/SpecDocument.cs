namespace FortressNexus.Web.Models.Entities;

public class SpecDocument
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public int Version { get; set; } = 1;
    public string Content { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public string GeneratedBy { get; set; } = "";
    public string? EditedContent { get; set; }
    public DateTime? EditedAt { get; set; }
    public string? EditedBy { get; set; }
    public bool IsApproved { get; set; } = false;
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public int PromptTokensUsed { get; set; } = 0;
    public int CompletionTokensUsed { get; set; } = 0;

    // Navigation
    public Submission? Submission { get; set; }
    public List<ArtifactSet> ArtifactSets { get; set; } = new();
}
