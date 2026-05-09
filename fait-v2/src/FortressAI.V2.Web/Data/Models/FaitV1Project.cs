namespace FortressAI.V2.Web.Data.Models;

/// <summary>DTO for reading FAIT v1 projects via cross-schema query.</summary>
public class FaitV1Project
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CustomInstructions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
