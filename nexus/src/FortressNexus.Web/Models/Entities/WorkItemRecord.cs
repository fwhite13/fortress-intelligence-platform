using FortressNexus.Web.Services;

namespace FortressNexus.Web.Models.Entities;

public class WorkItemRecord
{
    public int Id { get; set; }
    public int ArtifactSetId { get; set; }
    public int? AdoWorkItemId { get; set; }
    public string? AdoWorkItemUrl { get; set; }
    public string WorkItemType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "Created";
    public string? ErrorDetail { get; set; }

    // Predecessor links — JSON-serialized array of WI title strings
    public List<string>? PredecessorTitles { get; set; }

    // External dependency fields
    public bool IsExternalDependency { get; set; } = false;
    public string? ExternalOwner { get; set; }

    // Developer brief / description
    public string? Description { get; set; }

    // Acceptance criteria — newline-delimited string (e.g. "Item 1\nItem 2\nItem 3")
    public string? AcceptanceCriteria { get; set; }

    // Parent story title (for Test Cases linked to a User Story)
    public string? ParentTitle { get; set; }

    // Incremental decomp: true once this item has been enriched by a 1B batch
    public bool IsEnriched { get; set; } = false;

    // WI template classification
    public WiTemplateType WiTemplate { get; set; } = WiTemplateType.Standard;

    // Test Case relationship — JSON-serialized array of Test Case WI titles
    public List<string>? TestedByTitles { get; set; }

    // Navigation
    public ArtifactSet? ArtifactSet { get; set; }
}
