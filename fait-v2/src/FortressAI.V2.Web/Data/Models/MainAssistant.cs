using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("main_assistants")]
public class MainAssistant
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("soul_blob_path")]
    [MaxLength(500)]
    [Required]
    public string SoulBlobPath { get; set; } = string.Empty;

    [Column("memory_blob_path")]
    [MaxLength(500)]
    [Required]
    public string MemoryBlobPath { get; set; } = string.Empty;

    [Column("workspace_s3_prefix")]
    [MaxLength(500)]
    [Required]
    public string WorkspaceS3Prefix { get; set; } = string.Empty;

    [Column("fargate_session_id")]
    [MaxLength(200)]
    public string? FargateSessionId { get; set; }

    [Column("fargate_task_arn")]
    [MaxLength(500)]
    public string? FargateTaskArn { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
