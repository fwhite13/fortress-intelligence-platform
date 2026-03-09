namespace FortressAI.Shared.Models;

public class KbEntry
{
    public int Id { get; set; }
    public Guid UserId { get; set; }           // creator
    public int? TeamId { get; set; }            // null = personal; set = team
    public KbTier Tier { get; set; }            // Personal, Team, Corporate
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";   // TEXT — the KB entry body
    public string? Tags { get; set; }           // comma-separated
    public string? SourceUrl { get; set; }      // optional — where this came from
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum KbTier { Personal = 0, Team = 1, Corporate = 2 }
