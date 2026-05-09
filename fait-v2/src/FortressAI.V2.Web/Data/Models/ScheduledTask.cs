namespace FortressAI.V2.Web.Data.Models;

public class ScheduledTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "on_demand"; // "recurring" | "on_demand"
    public string? CronExpression { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastRunStatus { get; set; }  // "success" | "failed" | "cancelled" | "running"
    public int FailureCount { get; set; } = 0;
    public bool AlertOnCompletion { get; set; } = false;
    public bool AlertOnFailure { get; set; } = true;
    public bool IsActive { get; set; } = true;
    /// <summary>false = CC execution (default); true = IUserAgentRuntime SendTurnAsync execution</summary>
    public bool TaskMode { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
