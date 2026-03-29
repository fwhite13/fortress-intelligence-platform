namespace FortressIntelligenceRM.Web.Models;

public class UserMicrosoftToken
{
    public string UserId { get; set; } = null!;      // FIRM user id (char(36))
    public string AccessToken { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public string? MicrosoftEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
