namespace FortressAI.Shared.Models;

public class CalendarEvent
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Location { get; set; }
    public string? OnlineMeetingUrl { get; set; }
    public string? AttendeesJson { get; set; }
    public string? Category { get; set; }
    public DateTime LastFetchedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser? User { get; set; }
}
