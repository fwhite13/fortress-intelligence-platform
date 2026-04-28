using FortressNexus.Web.Services;

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

    // Predecessor links — JSON-serialized array of WI title strings
    public List<string>? PredecessorTitles { get; set; }

    // External dependency fields
    public bool IsExternalDependency { get; set; } = false;
    public string? ExternalOwner { get; set; }

    // WI template classification
    public WiTemplateType WiTemplate { get; set; } = WiTemplateType.Standard;

    // Test Case relationship — JSON-serialized array of Test Case WI titles
    public List<string>? TestedByTitles { get; set; }

    // Navigation
    public ArtifactSet? ArtifactSet { get; set; }
}
