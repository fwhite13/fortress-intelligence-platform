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
}

public record AdoProcessTemplate(string TypeId, string Name, string Description);
