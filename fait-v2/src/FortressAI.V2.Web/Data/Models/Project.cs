using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("projects")]
public class Project
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("name")]
    [MaxLength(200)]
    [Required]
    public string Name { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("v1_project_id")]
    public int? V1ProjectId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("custom_instructions")]
    public string? CustomInstructions { get; set; }

    [Column("model")]
    [MaxLength(100)]
    public string Model { get; set; } = "claude-sonnet-4-6";

    [Column("enable_fortress_kb")]
    public bool EnableFortressKb { get; set; } = false;

    [Column("enable_personal_kb")]
    public bool EnablePersonalKb { get; set; } = false;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public List<ProjectDocument> Documents { get; set; } = new();
    public List<ConversationTask> ConversationTasks { get; set; } = new();
}
