namespace FortressAI.Shared.Models;

public class UserAssistantConfig
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string AssistantName { get; set; } = "Assistant";
    public string AvatarId { get; set; } = "shield";
    public string ColorHex { get; set; } = "#d4af37";
    public string PersonalityPreset { get; set; } = "friendly";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public AppUser? User { get; set; }
}
