using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("messages")]
public class Message
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("conversation_id")]
    [MaxLength(36)]
    [Required]
    public string ConversationId { get; set; } = null!;

    [Column("role")]
    [MaxLength(20)]
    [Required]
    public string Role { get; set; } = null!;  // "user" | "assistant" | "system"

    [Column("content", TypeName = "longtext")]
    [Required]
    public string Content { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("compacted_at")]
    public DateTime? CompactedAt { get; set; }

    [Column("is_compaction_summary")]
    public bool IsCompactionSummary { get; set; } = false;

    [Column("session_type")]
    [MaxLength(10)]
    public string SessionType { get; set; } = "main";  // "main" | "plugin"

    [Column("plugin_agent_id")]
    [MaxLength(50)]
    public string? PluginAgentId { get; set; }

    [Column("token_count")]
    public int TokenCount { get; set; } = 0;

    // Navigation
    public Conversation Conversation { get; set; } = null!;
}
