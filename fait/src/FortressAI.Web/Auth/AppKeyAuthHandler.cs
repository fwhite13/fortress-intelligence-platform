using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FortressAI.Web.Auth;

/// <summary>
/// API key authentication handler for machine-to-machine endpoints (e.g., Haven PWA).
/// Reads the x-api-key header and validates it against the AppKeys:Haven configuration value.
/// Returns NoResult (not Fail) when the header is absent so other schemes (Cookie/OIDC) can still handle the request.
/// </summary>
public class AppKeyAuthOptions : AuthenticationSchemeOptions
{
    public string? ApiKey { get; set; }
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

        var configuredKey = Options.ApiKey;

        // Key is configured but doesn't match — explicit failure
        if (string.IsNullOrEmpty(configuredKey) || !string.Equals(apiKey, configuredKey, StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        // Valid key — issue claims for Fred White (the Haven PWA service account)
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "08de7605-3f7d-427d-858a-637777b41018"),
            new Claim("oid",                     "08de7605-3f7d-427d-858a-637777b41018"),
            new Claim(ClaimTypes.Email,          "fwhite@refugems.com"),
            new Claim(ClaimTypes.Name,           "Fred White"),
            new Claim("preferred_username",      "fwhite@refugems.com"),
            new Claim("groups",                  "FIP-Users"),
            new Claim("groups",                  "FAIT-Users")
        };

        var identity  = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
