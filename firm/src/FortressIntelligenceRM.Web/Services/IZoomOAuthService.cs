using FortressIntelligenceRM.Web.Models;

namespace FortressIntelligenceRM.Web.Services;

public interface IZoomOAuthService
{
    /// <summary>Returns the Zoom OAuth authorization URL to redirect the user to.</summary>
    string GetAuthorizationUrl(string state);

    /// <summary>
    /// Exchanges an auth code for tokens and fetches the Zoom profile, without storing anything.
    /// Used directly by the shared bot account flow (WI #8043), which persists to its own table.
    /// </summary>
    Task<ZoomTokenExchangeResult> ExchangeCodeAsync(string code);

    /// <summary>Refreshes an access token. Returns null on failure. RefreshToken is null if Zoom did not rotate it.</summary>
    Task<ZoomTokenRefreshResult?> RefreshTokensAsync(string refreshToken);

    /// <summary>
    /// Requests an OBF token for a meeting using a raw access token. authError is true on 401/403,
    /// signalling the caller should refresh and retry.
    /// </summary>
    Task<(bool ok, bool authError, string? token)> FetchObfTokenAsync(string accessToken, long meetingId);

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

    /// <summary>Returns the user's stored Zoom OAuth record (for status display), or null if not connected.</summary>
    Task<FirmZoomOAuth?> GetConnectionAsync(Guid userId);
}


public record ZoomTokenExchangeResult(string ZoomUserId, string? Email, string AccessToken, string RefreshToken, DateTime ExpiresAt);

public record ZoomTokenRefreshResult(string AccessToken, string? RefreshToken, DateTime ExpiresAt);
