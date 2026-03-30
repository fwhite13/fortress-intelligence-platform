namespace FortressNexus.Web.Models.Entities;

public class WorkItemRecord
{
    public int Id { get; set; }
    public int ArtifactSetId { get; set; }
    public int AdoWorkItemId { get; set; }
    public string AdoWorkItemUrl { get; set; } = "";
    public string WorkItemType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "Created";
    public string? ErrorDetail { get; set; }

    // Navigation
    public ArtifactSet? ArtifactSet { get; set; }
}
