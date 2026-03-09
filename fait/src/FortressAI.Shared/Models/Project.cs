namespace FortressAI.Shared.Models;

public class Project
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CustomInstructions { get; set; }
    public string Model { get; set; } = "claude-sonnet-4-6";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Knowledge Base flags (feature toggle — defaults off for zero behavior change)
    public bool EnableFortressKb { get; set; } = false;
    public bool EnablePersonalKb { get; set; } = false;

    // Navigation
    public AppUser? User { get; set; }
    public List<ProjectDocument> Documents { get; set; } = new();
    public List<Conversation> Conversations { get; set; } = new();
}
