namespace FortressAI.Shared.Models;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? Title { get; set; }
    public string Model { get; set; } = "claude-sonnet-4-6";
    public bool EnableFortressKb { get; set; } = false;
    public bool EnablePersonalKb { get; set; } = false;
    public List<ConversationTeamKb> TeamKbs { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser? User { get; set; }
    public Project? Project { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();
}
