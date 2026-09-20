using System.Security.Claims;
using System.Text.Json;
using FortressIntelligenceRM.Web.Models;
using FortressIntelligenceRM.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace FortressIntelligenceRM.Web.Controllers;

[Route("oauth/zoom")]
[Authorize]
public class ZoomOAuthController : Controller
{
    // State is a self-contained, DataProtection-signed token (userId + issued-at) rather than a
    // server-side session — FIRM has no ASP.NET session middleware configured, and this avoids
    // adding one just for a single-use CSRF value.
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    private readonly IZoomOAuthService _zoomOAuthService;
    private readonly MeetingService _meetingService;
    private readonly IDataProtector _stateProtector;
    private readonly ILogger<ZoomOAuthController> _logger;

    public ZoomOAuthController(
        IZoomOAuthService zoomOAuthService,
        MeetingService meetingService,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ZoomOAuthController> logger)
    {
        _zoomOAuthService = zoomOAuthService;
        _meetingService = meetingService;
        _stateProtector = dataProtectionProvider.CreateProtector("Firm.ZoomOAuthState");
        _logger = logger;
    }

    [HttpGet("authorize")]
    public async Task<IActionResult> Authorize()
    {
        var firmUser = await ResolveCurrentUserAsync();
        if (firmUser == null)
            return Unauthorized();

        var statePayload = JsonSerializer.Serialize(new ZoomOAuthState(firmUser.Id, DateTime.UtcNow));
        var state = _stateProtector.Protect(statePayload);

        return Redirect(_zoomOAuthService.GetAuthorizationUrl(state));
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BadRequest("Missing code or state");

        var firmUser = await ResolveCurrentUserAsync();
        if (firmUser == null)
            return Unauthorized();

        ZoomOAuthState? parsedState;
        try
        {
            var statePayload = _stateProtector.Unprotect(state);
            parsedState = JsonSerializer.Deserialize<ZoomOAuthState>(statePayload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FIRM: Zoom OAuth callback rejected — invalid state token");
            return BadRequest("Invalid or expired state");
        }

        if (parsedState == null || parsedState.UserId != firmUser.Id
            || DateTime.UtcNow - parsedState.IssuedAtUtc > StateLifetime)
        {
            _logger.LogWarning("FIRM: Zoom OAuth callback rejected — state mismatch or expired for user {UserId}", firmUser.Id);
            return BadRequest("Invalid or expired state");
        }

        try
        {
            var zoomEmail = await _zoomOAuthService.HandleCallbackAsync(firmUser.Id, code);
            _logger.LogInformation("FIRM: Zoom account linked for user {UserId} ({Email})", firmUser.Id, zoomEmail);
            return Redirect("/meetings?zoomConnected=1");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Zoom OAuth callback failed for user {UserId}", firmUser.Id);
            return Redirect("/meetings?zoomConnectError=1");
        }
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        var firmUser = await ResolveCurrentUserAsync();
        if (firmUser == null)
            return Unauthorized();

        await _zoomOAuthService.DisconnectAsync(firmUser.Id);
        return Redirect("/meetings?zoomDisconnected=1");
    }

    private async Task<FirmUser?> ResolveCurrentUserAsync()
    {
        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst("preferred_username")?.Value ?? "";
        var displayName = User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst("name")?.Value ?? email;

        if (string.IsNullOrEmpty(entraOid))
            return null;

        return await _meetingService.GetOrCreateUserAsync(entraOid, email, displayName);
    }

    private record ZoomOAuthState(Guid UserId, DateTime IssuedAtUtc);
}
