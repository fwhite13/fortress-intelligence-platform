using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.Shared.Models;

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Synthetic in-memory flag — not persisted to DB. True for resumption brief messages.</summary>
    [NotMapped]
    public bool IsResumptionBrief { get; set; } = false;

    // Navigation
    public Conversation? Conversation { get; set; }
}
