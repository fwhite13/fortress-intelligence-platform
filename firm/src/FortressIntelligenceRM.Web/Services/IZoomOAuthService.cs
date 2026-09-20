namespace FortressIntelligenceRM.Web.Services;

public interface IZoomOAuthService
{
    /// <summary>Returns the Zoom OAuth authorization URL to redirect the user to.</summary>
    string GetAuthorizationUrl(string state);

    /// <summary>Exchanges an auth code for tokens, stores them, and returns the linked Zoom email.</summary>
    Task<string> HandleCallbackAsync(Guid userId, string code);

    /// <summary>
    /// Gets an OBF (on-behalf-of) token for a specific meeting. Returns null if the user has no
    /// linked Zoom account, or if the token could not be obtained — a missing OBF token is
    /// non-fatal, the bot falls back to a JWT-only join.
    /// </summary>
    Task<string?> GetObfTokenAsync(Guid userId, long meetingId);

    /// <summary>Removes the user's linked Zoom account.</summary>
    Task DisconnectAsync(Guid userId);

    /// <summary>Returns the linked Zoom email for display, or null if not connected.</summary>
    Task<string?> GetLinkedEmailAsync(Guid userId);
}
