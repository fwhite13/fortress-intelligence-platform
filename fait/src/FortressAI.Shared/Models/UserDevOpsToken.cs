namespace FortressAI.Shared.Models;

public class UserDevOpsToken
{
    public Guid UserId { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? Email { get; set; }       // DevOps account email
    public string? DisplayName { get; set; } // DevOps account display name
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser? User { get; set; }
}
