namespace FortressAI.Shared.Models;

public class CreateScheduledTaskDto
{
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "on_demand"; // "recurring" | "on_demand"
    public string? CronExpression { get; set; }
    public Guid? ProjectId { get; set; }
    public bool AlertOnCompletion { get; set; } = false;
    public bool AlertOnFailure { get; set; } = true;
    public bool TaskMode { get; set; } = false;
}

public class UpdateScheduledTaskDto
{
    public string? Name { get; set; }
    public string? Prompt { get; set; }
    public string? CronExpression { get; set; }
    public Guid? ProjectId { get; set; }
    public bool? AlertOnCompletion { get; set; }
    public bool? AlertOnFailure { get; set; }
    public bool? TaskMode { get; set; }
}
