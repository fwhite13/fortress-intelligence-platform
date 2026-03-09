using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FortressAI.Web.Auth;

/// <summary>
/// DEV ONLY: Auto-authenticates all requests as Fred White.
/// Produces the same claims structure as real Entra OIDC.
/// Toggle via UseStubAuth=true in appsettings/env.
/// </summary>
public class StubAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public StubAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000001"),
            new Claim("oid", "00000000-0000-0000-0000-000000000001"),
            new Claim(ClaimTypes.Email, "fred@fortressam.ai"),
            new Claim(ClaimTypes.Name, "Fred White"),
            new Claim("preferred_username", "fred@fortressam.ai"),
            new Claim("groups", "FIP-Users"),
            new Claim("groups", "FAIT-Users")
        };

        var identity = new ClaimsIdentity(claims, "StubAuth");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "StubAuth");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
