namespace FortressAI.V2.Web.Data.Models;

public class ScheduledTaskRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TaskId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "running";  // "running" | "success" | "failed" | "cancelled"
    public string? ErrorMessage { get; set; }
    public string? ArtifactS3Key { get; set; }
    public string? SandboxId { get; set; }
    // Navigation
    public ScheduledTask? Task { get; set; }
}
