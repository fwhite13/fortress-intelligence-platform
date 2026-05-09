namespace FortressAI.Shared.Models;

public class UserAssistantConfig
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string AssistantName { get; set; } = "Assistant";
    public string AvatarId { get; set; } = "shield";
    public string ColorHex { get; set; } = "#d4af37";
    public string PersonalityPreset { get; set; } = "friendly";
    public bool FirmAutoTranscript { get; set; } = false;
    public bool FirmAutoSummary { get; set; } = false;
    public string? Role { get; set; }
    public string? Responsibilities { get; set; }
    public string? CommunicationStyle { get; set; }
    public string? ResponseFormat { get; set; }
    public bool? ShowCitations { get; set; }
    public string? UseCasesJson { get; set; }
    public string? AdditionalContext { get; set; }
    public string? PreferredName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public AppUser? User { get; set; }
}
