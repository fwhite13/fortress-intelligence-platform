namespace FortressIntelligenceRM.Web.Models;

public class UserMicrosoftToken
{
    public Guid UserId { get; set; } // PK, FK to firm_users
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string? MicrosoftEmail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public FirmUser? User { get; set; } // navigation
}
