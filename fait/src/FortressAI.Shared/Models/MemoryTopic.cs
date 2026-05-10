using FortressAI.Shared.Models;

namespace FortressAI.Shared.Models;

public class MemoryTopic
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Slug { get; set; } = string.Empty;    // VARCHAR(100), S3 filename without .md
    public string Title { get; set; } = string.Empty;   // VARCHAR(200), human-readable
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser? User { get; set; }
}
