namespace FortressAI.Shared.Models;

public class UserMicrosoftToken
{
    public Guid UserId { get; set; } // PK, FK to users
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string? MicrosoftEmail { get; set; } // display purposes
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser? User { get; set; }
}
