namespace FortressAI.V2.Web.Data.Models;

public class FeedbackSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;            // "bug" | "suggestion"
    public string Description { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string? ScreenshotS3Key { get; set; }
    public string Status { get; set; } = "pending";             // "pending" | "triaged" | "dispatched" | "escalated"
    public string? AdoWiId { get; set; }
    public string? TriageResult { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TriagedAt { get; set; }
}
