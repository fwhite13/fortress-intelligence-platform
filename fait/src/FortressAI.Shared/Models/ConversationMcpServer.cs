namespace FortressAI.Shared.Models;

public class ConversationMcpServer
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ServerId { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Conversation? Conversation { get; set; }
    public McpServer? Server { get; set; }
}
