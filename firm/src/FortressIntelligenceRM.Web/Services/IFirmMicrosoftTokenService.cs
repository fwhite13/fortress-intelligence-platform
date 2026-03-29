using FortressIntelligenceRM.Web.Models;

namespace FortressIntelligenceRM.Web.Services;

public interface IFirmMicrosoftTokenService
{
    bool IsConfigured { get; }
    string GetAuthorizationUrl(string redirectUri, string state);
    Task<string?> GetValidAccessTokenAsync(string firmUserId);
    Task<UserMicrosoftToken> ExchangeCodeAsync(string firmUserId, string code, string redirectUri);
    Task RevokeTokenAsync(string firmUserId);
    bool HasToken(string firmUserId);
}
