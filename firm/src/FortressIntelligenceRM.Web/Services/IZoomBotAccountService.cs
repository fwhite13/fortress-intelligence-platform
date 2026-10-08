using FortressIntelligenceRM.Web.Models;

namespace FortressIntelligenceRM.Web.Services;

public interface IZoomBotAccountService
{
    /// <summary>Returns the shared Zoom bot account, or null if none is connected.</summary>
    Task<FirmZoomBotAccount?> GetAsync();

    /// <summary>Inserts or replaces the shared Zoom bot account.</summary>
    Task SaveAsync(string? email, string zoomUserId, string accessToken, string refreshToken, DateTime expiresAt);

    /// <summary>Removes the shared Zoom bot account.</summary>
    Task ClearAsync();

    /// <summary>
    /// Gets an OBF token for a meeting using the bot account, refreshing its access token if needed.
    /// Returns null if no bot account is connected or the token could not be obtained.
    /// </summary>
    Task<string?> GetObfTokenAsync(long meetingId);

    /// <summary>Returns the connected bot account's email, or null if not connected.</summary>
    Task<string?> GetEmailAsync();
}
