namespace FortressIntelligenceRM.Web.Models;

// WI #8043 — the shared Zoom bot account's OAuth tokens. Single row (Id = 1), deliberately not
// associated with any FIRM user so it can't mirror a user's personal Zoom connection.
public class FirmZoomBotAccount
{
    public int Id { get; set; } = 1;
    public string? Email { get; set; }
    public string? ZoomUserId { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime ConnectedAt { get; set; }
}
