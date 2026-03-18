using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FortressAI.Web.Auth;

/// <summary>
/// API key authentication handler for machine-to-machine endpoints (e.g., Haven PWA, Excel Add-in).
/// Reads the x-api-key header and validates it against the configured AppKeys.
///
/// Supports multiple valid keys via <see cref="AppKeyAuthOptions.ApiKeys"/> (Sprint 1: Excel Add-in).
/// Legacy single-key field <see cref="AppKeyAuthOptions.ApiKey"/> remains supported for backward compatibility.
/// Returns NoResult (not Fail) when the header is absent so other schemes (Cookie/OIDC) can still handle the request.
/// </summary>
public class AppKeyAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>Legacy single API key — Haven PWA service account.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Additional valid API keys (e.g., AppKeys:ExcelAddin).</summary>
    public List<string> ApiKeys { get; set; } = new();

    /// <summary>
    /// Resolved set of all valid keys: ApiKeys list + ApiKey (if set).
    /// Filters empty/null entries.
    /// </summary>
    public IEnumerable<string> AllKeys =>
        ApiKeys
            .Concat(string.IsNullOrEmpty(ApiKey) ? Array.Empty<string>() : new[] { ApiKey })
            .Where(k => !string.IsNullOrEmpty(k));
}

public class AppKeyAuthHandler : AuthenticationHandler<AppKeyAuthOptions>
{
    public AppKeyAuthHandler(
        IOptionsMonitor<AppKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = Request.Headers["x-api-key"].FirstOrDefault();

        // No header present — let other schemes try (Cookie, OIDC)
        if (string.IsNullOrEmpty(apiKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        // Check against all configured keys
        var allKeys = Options.AllKeys.ToList();

        if (allKeys.Count == 0 || !allKeys.Any(k => string.Equals(apiKey, k, StringComparison.Ordinal)))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        // Check if this is the FfE Excel Addin key (not the Haven key)
        var isExcelAddinKey = Options.ApiKeys.Contains(apiKey);
        var claims = isExcelAddinKey
            ? new[]
              {
                // Service-level identity for CI/testing — no personal KB access
                new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000001"),
                new Claim(ClaimTypes.Name,           "FfE Service Account"),
                new Claim(ClaimTypes.Email,          "ffe-service@internal"),
              }
            : new[]
              {
                // Haven key — existing Fred White claims (unchanged for backward compat)
                new Claim(ClaimTypes.NameIdentifier, "08de7605-3f7d-427d-858a-637777b41018"),
                new Claim("oid",                     "08de7605-3f7d-427d-858a-637777b41018"),
                new Claim(ClaimTypes.Email,          "fwhite@refugems.com"),
                new Claim(ClaimTypes.Name,           "Fred White"),
                new Claim("preferred_username",      "fwhite@refugems.com"),
                new Claim("groups",                  "FIP-Users"),
                new Claim("groups",                  "FAIT-Users"),
              };

        var identity  = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
