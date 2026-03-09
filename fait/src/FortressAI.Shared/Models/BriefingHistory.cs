namespace FortressAI.Shared.Models;

public class BriefingHistory
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly BriefingDate { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? EmailSummary { get; set; }
    public string? CalendarEventsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AppUser? User { get; set; }
}
