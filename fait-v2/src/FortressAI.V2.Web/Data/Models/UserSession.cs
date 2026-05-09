using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("user_sessions")]
public class UserSession
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("started_at")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [Column("last_active_at")]
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    [Column("ended_at")]
    public DateTime? EndedAt { get; set; }

    [Column("ip_address")]
    [MaxLength(50)]
    public string? IpAddress { get; set; }

    [Column("user_agent")]
    [MaxLength(500)]
    public string? UserAgent { get; set; }

    // Fargate runtime fields
    [Column("task_arn")]
    [MaxLength(500)]
    public string? TaskArn { get; set; }

    [Column("private_ip")]
    [MaxLength(45)]
    public string? PrivateIp { get; set; }

    [Column("fargate_status")]
    [MaxLength(20)]
    public string? FargateStatus { get; set; }

    [Column("fargate_session_id")]
    [MaxLength(200)]
    public string? FargateSessionId { get; set; }

    [Column("task_definition_revision")]
    [MaxLength(100)]
    public string? TaskDefinitionRevision { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
