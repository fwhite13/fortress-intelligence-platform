using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("kb_entries")]
public class KbEntry
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = "";

    [Column("team_id")]
    [MaxLength(36)]
    public string? TeamId { get; set; }

    [Column("tier")]
    public KbTier Tier { get; set; }

    [Column("title")]
    [MaxLength(500)]
    public string Title { get; set; } = "";

    [Column("content", TypeName = "longtext")]
    public string Content { get; set; } = "";

    [Column("tags")]
    [MaxLength(1000)]
    public string? Tags { get; set; }

    [Column("source_url")]
    [MaxLength(2000)]
    public string? SourceUrl { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public KbTeam? Team { get; set; }
}

public enum KbTier { Personal = 0, Team = 1, Corporate = 2, Developer = 3 }
