using System.ComponentModel.DataAnnotations;

namespace FortressAI.Shared.Models;

public class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = "user";
    public bool IsEntraUser { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLogin { get; set; }

    [MaxLength(255)]
    public string? EntraOid { get; set; }

    // Navigation
    public List<Project> Projects { get; set; } = new();
    public List<Conversation> Conversations { get; set; } = new();
    public UserAssistantConfig? AssistantConfig { get; set; }
    public UserBriefingSchedule? BriefingSchedule { get; set; }
}
