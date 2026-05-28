using System.Security.Cryptography;
using System.Text;

namespace FortressAI.Web.Services;

/// <summary>
/// Provides HMAC-SHA256 token generation and validation for artifact preview URLs.
/// Token format: base64url(HMAC-SHA256("{artifactId}:{userId}:{expires}"))
/// where expires is a Unix timestamp (seconds).
/// Token validity: 15 minutes from generation.
/// PREVIEW_TOKEN_SECRET env var is the HMAC key.
/// </summary>
public class ArtifactPreviewService
{
    private readonly string _secret;
    private readonly ILogger<ArtifactPreviewService> _logger;
    private const int TokenValiditySeconds = 900; // 15 minutes

    public ArtifactPreviewService(IConfiguration config, ILogger<ArtifactPreviewService> logger)
    {
        _secret = config["PREVIEW_TOKEN_SECRET"] ?? "";
        _logger = logger;
    }

    /// <summary>
    /// Generates a preview token for the given artifact and user.
    /// Returns (token, expiresUnixTimestamp).
    /// </summary>
    public (string token, long expires) GenerateToken(Guid artifactId, Guid userId)
    {
        var expires = DateTimeOffset.UtcNow.AddSeconds(TokenValiditySeconds).ToUnixTimeSeconds();
        var payload = $"{artifactId}:{userId}:{expires}";
        var token = ComputeHmac(payload);
        return (token, expires);
    }

    /// <summary>
    /// Validates a preview token. Returns true if valid and not expired.
    /// </summary>
    public bool ValidateToken(Guid artifactId, Guid userId, string token, long expires)
    {
        // Check expiry
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now > expires)
        {
            _logger.LogDebug("[ArtifactPreview] Token expired for artifact {ArtifactId}", artifactId);
            return false;
        }

        // Recompute HMAC and compare
        var payload = $"{artifactId}:{userId}:{expires}";
        var expected = ComputeHmac(payload);
        return CryptographicEquals(expected, token);
    }

    private string ComputeHmac(string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_secret);
        var msgBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(msgBytes);
        // Use base64url (no padding, URL-safe)
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool CryptographicEquals(string a, string b)
    {
        // Constant-time comparison to prevent timing attacks
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
