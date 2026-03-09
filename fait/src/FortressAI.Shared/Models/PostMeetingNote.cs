namespace FortressAI.Shared.Models;

public class PostMeetingNote
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string EventId { get; set; } = string.Empty;    // Graph calendar event ID
    public string EventSubject { get; set; } = string.Empty;
    public DateTime MeetingEndTime { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? Summary { get; set; }                   // Bedrock-generated, nullable
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser? User { get; set; }
}
