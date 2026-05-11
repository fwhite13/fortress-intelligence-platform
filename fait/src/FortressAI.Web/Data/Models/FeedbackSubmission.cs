using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.Web.Data.Models;

[Table("feedback_submissions")]
public class FeedbackSubmission
{
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(255)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;  // "bug" | "feature"

    [Required]
    public string Description { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? PageUrl { get; set; }

    [MaxLength(500)]
    public string? ScreenshotS3Key { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "pending";  // "pending" | "triaged" | "dispatched" | "escalated"

    public int? AdoWiId { get; set; }

    public string? TriageResult { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? TriagedAt { get; set; }
}
