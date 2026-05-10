using System;
using System.Collections.Generic;

namespace FortressAI.Shared.Models;

public class ScheduledTask
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "on_demand"; // 'recurring' | 'on_demand'
    public string? CronExpression { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastRunStatus { get; set; } // 'success' | 'failed' | 'cancelled' | NULL
    public int FailureCount { get; set; } = 0;
    public bool AlertOnCompletion { get; set; } = false;
    public bool AlertOnFailure { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public bool TaskMode { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public AppUser? User { get; set; }
    public Project? Project { get; set; }
    public List<ScheduledTaskRun> Runs { get; set; } = new();
}
