using FortressIntelligenceRM.Web.Models;

namespace FortressIntelligenceRM.Web.Services;

public interface IFirmMicrosoftTokenService
{
    bool IsConfigured { get; }
    string GetAuthorizationUrl(string redirectUri, string state);
    Task<string?> GetValidAccessTokenAsync(Guid firmUserId);
    Task<UserMicrosoftToken> ExchangeCodeAsync(Guid firmUserId, string code, string redirectUri);
    Task RevokeTokenAsync(Guid firmUserId);
    bool HasToken(Guid firmUserId);
}
