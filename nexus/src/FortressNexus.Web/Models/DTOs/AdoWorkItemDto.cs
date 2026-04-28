using FortressNexus.Web.Services;

namespace FortressNexus.Web.Models.DTOs;

public class AdoWorkItemDto
{
    public string WorkItemType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public int? StoryPoints { get; set; }
    public string? ParentTitle { get; set; }
    public List<string> Tags { get; set; } = new();

    // Classification fields (set by ArtifactGenerationService post-parse)
    public WiTemplateType WiTemplate { get; set; } = WiTemplateType.Standard;
    public bool IsExternalDependency { get; set; }
    public string? ExternalOwner { get; set; }
    public List<string>? TestedByTitles { get; set; }
}

public record AdoProcessTemplate(string TypeId, string Name, string Description);
