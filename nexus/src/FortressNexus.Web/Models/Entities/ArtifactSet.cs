using FortressNexus.Web.Models.Enums;

namespace FortressNexus.Web.Models.Entities;

public class ArtifactSet
{
    public int Id { get; set; }
    public int SpecDocumentId { get; set; }
    public string AdoOrganization { get; set; } = "";
    public string AdoProjectName { get; set; } = "";
    public string? AdoProjectId { get; set; }
    public string ProcessTemplateTypeId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public ArtifactSetStatus Status { get; set; } = ArtifactSetStatus.Pending;
    public string? ErrorDetail { get; set; }
    public int ExternalDependencyCount { get; set; } = 0;

    // Navigation
    public SpecDocument? SpecDocument { get; set; }
    public List<WorkItemRecord> WorkItemRecords { get; set; } = new();
}
