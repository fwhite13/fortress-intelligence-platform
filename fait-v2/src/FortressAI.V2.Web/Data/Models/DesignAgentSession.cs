using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("design_agent_sessions")]
public class DesignAgentSession
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("conversation_id")]
    [MaxLength(36)]
    public string? ConversationId { get; set; }

    [Column("stitch_project_id")]
    [MaxLength(200)]
    public string? StitchProjectId { get; set; }

    [Column("design_dna", TypeName = "TEXT")]
    public string? DesignDna { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
    public ICollection<DesignAgentArtifact> Artifacts { get; set; } = new List<DesignAgentArtifact>();
}
