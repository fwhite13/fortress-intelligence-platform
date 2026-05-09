using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("scheduled_task_approvals")]
public class ScheduledTaskApproval
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("scheduled_task_id")]
    [MaxLength(36)]
    [Required]
    public string ScheduledTaskId { get; set; } = string.Empty;

    [Column("intervention_id")]
    [MaxLength(36)]
    [Required]
    public string InterventionId { get; set; } = string.Empty;

    [Column("action_type")]
    [MaxLength(100)]
    public string ActionType { get; set; } = string.Empty;

    [Column("action_summary")]
    [MaxLength(2000)]
    public string ActionSummary { get; set; } = string.Empty;

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "pending"; // pending | approved | denied | expired

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }
}
