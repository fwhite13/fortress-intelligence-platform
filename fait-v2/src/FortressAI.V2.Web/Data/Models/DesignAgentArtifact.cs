using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("design_agent_artifacts")]
public class DesignAgentArtifact
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("session_id")]
    [MaxLength(36)]
    [Required]
    public string SessionId { get; set; } = string.Empty;

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("artifact_name")]
    [MaxLength(200)]
    [Required]
    public string ArtifactName { get; set; } = string.Empty;

    [Column("s3_key")]
    [MaxLength(500)]
    [Required]
    public string S3Key { get; set; } = string.Empty;

    [Column("stitch_screen_id")]
    [MaxLength(200)]
    public string? StitchScreenId { get; set; }

    [Column("is_fallback")]
    public bool IsFallback { get; set; } = false;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public DesignAgentSession? Session { get; set; }
}
