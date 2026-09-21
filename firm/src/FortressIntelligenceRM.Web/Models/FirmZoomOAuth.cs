namespace FortressIntelligenceRM.Web.Models;

public class FirmZoomOAuth
{
    public uint Id { get; set; }
    public Guid UserId { get; set; }
    public string ZoomUserId { get; set; } = "";
    public string? ZoomEmail { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
