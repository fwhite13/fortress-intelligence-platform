using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("memory_topics")]
public class MemoryTopic
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("topic_name")]
    [MaxLength(200)]
    [Required]
    public string TopicName { get; set; } = string.Empty;

    [Column("topic_slug")]
    [MaxLength(200)]
    [Required]
    public string TopicSlug { get; set; } = string.Empty;

    [Column("blob_path")]
    [MaxLength(500)]
    [Required]
    public string BlobPath { get; set; } = string.Empty;

    [Column("last_updated_at")]
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
