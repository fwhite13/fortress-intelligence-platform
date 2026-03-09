namespace FortressAI.Shared.Models;

public class ConversationTeamKb
{
    public Guid ConversationId { get; set; }
    public int TeamId { get; set; }
    public DateTime EnabledAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Conversation? Conversation { get; set; }
    public KbTeam? Team { get; set; }
}
