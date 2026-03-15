using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FortressAI.Web.Models;
using FortressAI.Web.Services;

namespace FortressAI.Web.Controllers;

[ApiController]
[Route("auth")]
[AllowAnonymous]
public class TestAuthController : ControllerBase
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TestAuthController> _logger;

    public TestAuthController(IServiceProvider services, ILogger<TestAuthController> logger)
    {
        _services = services;
        _logger = logger;
    }

    [EnableRateLimiting("test-auth")]
    [HttpPost("test-session")]
    public async Task<IActionResult> CreateTestSession([FromBody] TestSessionRequest request)
    {
        // Guard: only available in Development. Returns 404 in all other environments.
        var testAuth = _services.GetService<TestAuthService>();
        if (testAuth == null)
            return NotFound();

        if (!testAuth.ValidateSecret(request.Secret))
        {
            _logger.LogWarning("TestAuth: invalid secret attempt from {IP}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid secret" });
        }

        var principal = testAuth.BuildTestPrincipal(request.UserId, request.DisplayName);

        _logger.LogInformation("TestAuth: creating test session for {UserId} from {IP}",
            request.UserId, HttpContext.Connection.RemoteIpAddress);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        return Ok(new
        {
            message = "Test session created",
            userId = request.UserId,
            expiresIn = "8 hours"
        });
    }
}
