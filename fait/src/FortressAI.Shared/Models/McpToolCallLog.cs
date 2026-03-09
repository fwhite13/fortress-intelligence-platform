namespace FortressAI.Shared.Models;

public class McpToolCallLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public Guid ServerId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string Status { get; set; } = "success";
    public string? ErrorMessage { get; set; }
    public int? LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; }
}
