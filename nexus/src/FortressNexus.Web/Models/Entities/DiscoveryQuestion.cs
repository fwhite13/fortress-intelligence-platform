namespace FortressNexus.Web.Models.Entities;

public class DiscoveryQuestion
{
    public Guid Id { get; set; }
    public Guid DiscoverySessionId { get; set; }
    public int SortOrder { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
    public string? Rationale { get; set; }
    public DateTime CreatedAt { get; set; }

    public DiscoverySession Session { get; set; } = null!;
    public DiscoveryAnswer? Answer { get; set; }
}
