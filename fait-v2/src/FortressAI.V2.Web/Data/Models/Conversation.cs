using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("conversations")]
public class Conversation
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = null!;

    [Column("title")]
    [MaxLength(500)]
    public string? Title { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_active_at")]
    public DateTime? LastActiveAt { get; set; }

    [Column("estimated_token_count")]
    public int EstimatedTokenCount { get; set; } = 0;

    // Navigation
    public User User { get; set; } = null!;
    public List<Message> Messages { get; set; } = [];
}
