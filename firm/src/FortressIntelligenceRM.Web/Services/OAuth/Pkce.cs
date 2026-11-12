using System.Security.Cryptography;
using System.Text;

namespace FortressIntelligenceRM.Web.Services.OAuth;

/// <summary>WI #9133 — RFC 7636 PKCE helpers (S256 only).</summary>
public static class Pkce
{
    public const string Method = "S256";

    /// <summary>A 43-character base64url verifier from 32 random bytes.</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>BASE64URL(SHA256(ASCII(verifier))).</summary>
    public static string ChallengeS256(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
