using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace FortressIntelligenceRM.Web.Services.OAuth;

/// <summary>
/// WI #9133 — encrypts OAuth tokens at rest with DataProtection under a per-provider purpose
/// (e.g. "Firm.PlaudTokens.v1", spec §9.2). Tokens are written to the DB only through Protect.
/// FIRM's key ring has DisableAutomaticKeyGeneration, so a lost key surfaces as a
/// <see cref="CryptographicException"/> from Unprotect; callers treat that as "reconnect needed".
/// </summary>
public class TokenProtector
{
    private readonly IDataProtector _protector;

    public TokenProtector(IDataProtectionProvider provider, string purpose)
    {
        _protector = provider.CreateProtector(purpose);
    }

    public string Protect(string token) => _protector.Protect(token ?? "");

    /// <exception cref="CryptographicException">The value was not produced by this purpose/key ring.</exception>
    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);

    public bool TryUnprotect(string? protectedToken, out string token)
    {
        token = "";
        if (string.IsNullOrEmpty(protectedToken))
            return false;
        try
        {
            token = _protector.Unprotect(protectedToken);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
