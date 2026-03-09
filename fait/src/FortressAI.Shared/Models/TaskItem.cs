namespace FortressAI.Shared.Models;

public class TaskItem
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public int PercentComplete { get; set; }
    public int Priority { get; set; } = 5;
    public string? PlanTitle { get; set; }
    public string? BucketName { get; set; }
    public DateTime LastFetchedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser? User { get; set; }
}
