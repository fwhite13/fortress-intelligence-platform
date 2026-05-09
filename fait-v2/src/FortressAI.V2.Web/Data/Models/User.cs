using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("users")]
public class User
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("entra_oid")]
    [MaxLength(255)]
    public string? EntraOid { get; set; }

    [Column("email")]
    [MaxLength(255)]
    [Required]
    public string Email { get; set; } = string.Empty;

    [Column("display_name")]
    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [Column("onboarding_completed_at")]
    public DateTime? OnboardingCompletedAt { get; set; }

    [Column("onboarding_step")]
    public int? OnboardingStep { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("avatar_url")]
    [MaxLength(1000)]
    public string? AvatarUrl { get; set; }

    // Navigation
    public MainAssistant? MainAssistant { get; set; }
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<MemoryTopic> MemoryTopics { get; set; } = new List<MemoryTopic>();
    public ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();
}
