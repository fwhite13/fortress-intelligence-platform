using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("pushed_messages")]
public class PushedMessage
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("source")]
    [MaxLength(50)]
    [Required]
    public string Source { get; set; } = string.Empty;

    [Column("title")]
    [MaxLength(500)]
    [Required]
    public string Title { get; set; } = string.Empty;

    [Column("content", TypeName = "TEXT")]
    [Required]
    public string Content { get; set; } = string.Empty;

    [Column("external_id")]
    [MaxLength(100)]
    public string? ExternalId { get; set; }

    [Column("is_read")]
    public bool IsRead { get; set; } = false;

    [Column("meeting_date")]
    public DateTime MeetingDate { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
}
