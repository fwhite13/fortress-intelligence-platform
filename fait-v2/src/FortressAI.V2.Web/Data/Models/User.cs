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
    [MaxLength(100)]
    [Required]
    public string EntraOid { get; set; } = string.Empty;

    [Column("email")]
    [MaxLength(200)]
    [Required]
    public string Email { get; set; } = string.Empty;

    [Column("display_name")]
    [MaxLength(200)]
    [Required]
    public string DisplayName { get; set; } = string.Empty;

    [Column("onboarding_completed_at")]
    public DateTime? OnboardingCompletedAt { get; set; }

    [Column("onboarding_step")]
    public int? OnboardingStep { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public MainAssistant? MainAssistant { get; set; }
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<MemoryTopic> MemoryTopics { get; set; } = new List<MemoryTopic>();
    public ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();
}
