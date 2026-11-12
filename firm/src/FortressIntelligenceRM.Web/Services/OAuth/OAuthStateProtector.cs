using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace FortressIntelligenceRM.Web.Services.OAuth;

/// <summary>A state payload carries its own issued-at time so the protector can enforce a lifetime.</summary>
public interface IOAuthStatePayload
{
    DateTime IssuedAtUtc { get; }
}

/// <summary>
/// WI #9133 — stateless OAuth state (Zoom pattern, spec §2.1/§2.4): the payload is JSON sealed with
/// DataProtection (authenticated encryption, so a PKCE verifier inside it stays secret), under a
/// per-provider purpose, and rejected once older than the lifetime. No session or server cache, so it
/// works across tasks. Unprotect never throws and never logs the state value.
/// </summary>
public class OAuthStateProtector
{
    private readonly IDataProtector _protector;
    private readonly TimeSpan _lifetime;

    public OAuthStateProtector(IDataProtectionProvider provider, string purpose, TimeSpan lifetime)
    {
        _protector = provider.CreateProtector(purpose);
        _lifetime = lifetime;
    }

    public TimeSpan Lifetime => _lifetime;

    public string Protect<T>(T payload) where T : IOAuthStatePayload =>
        _protector.Protect(JsonSerializer.Serialize(payload));

    /// <summary>Returns the payload, or null when the state is tampered, unreadable, from the future or expired.</summary>
    public T? Unprotect<T>(string? state) where T : class, IOAuthStatePayload
    {
        if (string.IsNullOrEmpty(state))
            return null;
        T? payload;
        try
        {
            payload = JsonSerializer.Deserialize<T>(_protector.Unprotect(state));
        }
        catch
        {
            return null;
        }
        if (payload == null)
            return null;
        var age = DateTime.UtcNow - payload.IssuedAtUtc;
        if (age > _lifetime || age < TimeSpan.FromMinutes(-1))
            return null;
        return payload;
    }
}
