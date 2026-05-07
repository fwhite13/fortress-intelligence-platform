namespace FortressAI.V2.Web.Services;

public interface ICCExecutionService
{
    Task<CCExecutionResult> DispatchTaskAsync(
        string userId,
        string task,
        CCContextEnvelope contextEnvelope,
        IProgress<CCProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}

public class CCContextEnvelope
{
    public string UserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public List<string> KbIds { get; set; } = new();
    public List<string> EnabledMcpServers { get; set; } = new();
    public string? MemorySummary { get; set; }
    public string TaskInstructions { get; set; } = string.Empty;
}

public class CCExecutionResult
{
    public bool Success { get; set; }
    public string? ArtifactS3Key { get; set; }
    public string? ArtifactType { get; set; }  // "html", "docx", "xlsx", "pptx", "json", "code"
    public string? Output { get; set; }         // last CC stdout text
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
}

public class CCProgressUpdate
{
    public string TaskId { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public List<string> CompletedSteps { get; set; } = new();
    public List<string> ToolCallsMade { get; set; } = new();
    public TimeSpan ElapsedTime { get; set; }
    public string? InterventionPrompt { get; set; }
}
