using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FortressAI.Web.Services;

public class TestAuthService
{
    private readonly IConfiguration _config;
    private readonly ILogger<TestAuthService> _logger;

    public TestAuthService(IConfiguration config, ILogger<TestAuthService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool ValidateSecret(string secret)
    {
        var expected = _config["TestAuth:Secret"];
        if (string.IsNullOrEmpty(expected)) return false;
        return string.Equals(secret, expected, StringComparison.Ordinal);
    }

    public ClaimsPrincipal BuildTestPrincipal(string userId, string displayName)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Email, userId),
            new("preferred_username", userId),
            new("http://schemas.microsoft.com/identity/claims/objectidentifier",
                Guid.NewGuid().ToString()),
            new("tid", _config["AzureAd:TenantId"] ?? "test-tenant"),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
