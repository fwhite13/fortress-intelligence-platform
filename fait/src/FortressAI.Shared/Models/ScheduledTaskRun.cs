using System;

namespace FortressAI.Shared.Models;

public class ScheduledTaskRun
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty; // 'success' | 'failed' | 'cancelled'
    public string? Error { get; set; }
    public string? ResultSummary { get; set; }
    public string? ArtifactBlobPath { get; set; }
    public string? SandboxId { get; set; }

    // Navigation
    public ScheduledTask? Task { get; set; }
}
