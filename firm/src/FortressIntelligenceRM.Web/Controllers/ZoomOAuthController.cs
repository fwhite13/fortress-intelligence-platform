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
    private readonly IZoomBotAccountService _zoomBotAccountService;
    private readonly AdminAccessService _adminAccess;
    private readonly IDataProtector _stateProtector;
    private readonly ILogger<ZoomOAuthController> _logger;

    public ZoomOAuthController(
        IZoomOAuthService zoomOAuthService,
        MeetingService meetingService,
        IZoomBotAccountService zoomBotAccountService,
        AdminAccessService adminAccess,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ZoomOAuthController> logger)
    {
        _zoomOAuthService = zoomOAuthService;
        _meetingService = meetingService;
        _zoomBotAccountService = zoomBotAccountService;
        _adminAccess = adminAccess;
        _stateProtector = dataProtectionProvider.CreateProtector("Firm.ZoomOAuthState");
        _logger = logger;
    }

    // bot=true (WI #8034, admin-only): the linked account becomes the shared Zoom bot account,
    // stored in firm_zoom_bot_account with no user association (WI #8043).
    // The flag travels inside the signed state — Zoom's redirect URI is fixed, so a query param on
    // /authorize would not survive to /callback.
    [HttpGet("authorize")]
    public async Task<IActionResult> Authorize([FromQuery] bool bot = false)
    {
        var firmUser = await ResolveCurrentUserAsync();
        if (firmUser == null)
            return Unauthorized();
        if (bot && !await _adminAccess.IsAdminAsync(User))
            return Forbid();

        var statePayload = JsonSerializer.Serialize(new ZoomOAuthState(firmUser.Id, DateTime.UtcNow, bot));
        var state = _stateProtector.Protect(statePayload);

        return Redirect(_zoomOAuthService.GetAuthorizationUrl(state));
    }

    // Anonymous: Zoom's cross-site redirect may not carry the FIRM session cookie (SameSite). Identity
    // comes solely from the DataProtection-signed state token, which is the CSRF protection.
    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BadRequest("Missing code or state");

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

        if (parsedState == null || DateTime.UtcNow - parsedState.IssuedAtUtc > StateLifetime)
        {
            _logger.LogWarning("FIRM: Zoom OAuth callback rejected — state missing or expired");
            return BadRequest("Invalid or expired state");
        }

        try
        {
            if (parsedState.IsBot)
            {
                var bot = await _zoomOAuthService.ExchangeCodeAsync(code);
                await _zoomBotAccountService.SaveAsync(bot.Email, bot.ZoomUserId, bot.AccessToken, bot.RefreshToken, bot.ExpiresAt);
                _logger.LogInformation("FIRM: Zoom bot account connected as {Email} by user {UserId}", bot.Email ?? bot.ZoomUserId, parsedState.UserId);
                return Redirect("/admin/zoom?zoomConnected=1");
            }

            var zoomEmail = await _zoomOAuthService.HandleCallbackAsync(parsedState.UserId, code);
            _logger.LogInformation("FIRM: Zoom account linked for user {UserId} ({Email})", parsedState.UserId, zoomEmail);
            return Redirect("/meetings?zoomConnected=1");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Zoom OAuth callback failed for user {UserId}", parsedState.UserId);
            return Redirect(parsedState.IsBot ? "/admin/zoom?zoomConnectError=1" : "/meetings?zoomConnectError=1");
        }
    }

    // bot=true (WI #8034, admin-only): disconnects the shared Zoom bot account, whichever admin linked it.
    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect([FromForm] bool bot = false)
    {
        var firmUser = await ResolveCurrentUserAsync();
        if (firmUser == null)
            return Unauthorized();

        if (bot)
        {
            if (!await _adminAccess.IsAdminAsync(User))
                return Forbid();

            await _zoomBotAccountService.ClearAsync();
            _logger.LogInformation("FIRM: Zoom bot account disconnected by {UserId}", firmUser.Id);
            return Redirect("/admin/zoom?zoomDisconnected=1");
        }

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

    private record ZoomOAuthState(Guid UserId, DateTime IssuedAtUtc, bool IsBot = false);
}
