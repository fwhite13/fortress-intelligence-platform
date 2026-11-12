using System.Security.Claims;
using FortressIntelligenceRM.Web.Models;
using FortressIntelligenceRM.Web.Services;
using FortressIntelligenceRM.Web.Services.OAuth;
using FortressIntelligenceRM.Web.Services.Plaud;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace FortressIntelligenceRM.Web.Controllers;

/// <summary>
/// WI #9133 — per-user Plaud account linking. Mirrors ZoomOAuthController (spec §2.1): stateless
/// DataProtection state with a 15-minute lifetime, anonymous callback whose identity comes only from
/// that state. The PKCE verifier travels sealed inside the state (authenticated encryption), so no
/// server-side storage is needed for the round trip. Every action is 404 when Firm:Plaud:Enabled is false.
/// </summary>
[Route("oauth/plaud")]
[Authorize]
public class PlaudOAuthController : Controller
{
    public const string StatePurpose = "Firm.PlaudOAuthState";
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    private readonly IPlaudOAuthService _plaud;
    private readonly MeetingService _meetingService;
    private readonly PlaudSyncService _sync;
    private readonly OAuthStateProtector _stateProtector;
    private readonly ILogger<PlaudOAuthController> _logger;

    public PlaudOAuthController(
        IPlaudOAuthService plaud,
        MeetingService meetingService,
        PlaudSyncService sync,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<PlaudOAuthController> logger)
    {
        _plaud = plaud;
        _meetingService = meetingService;
        _sync = sync;
        _stateProtector = new OAuthStateProtector(dataProtectionProvider, StatePurpose, StateLifetime);
        _logger = logger;
    }

    [HttpGet("authorize")]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        if (!_plaud.IsEnabled)
            return NotFound();

        var firmUser = await ResolveCurrentUserAsync();
        if (firmUser == null)
            return Unauthorized();

        var verifier = Pkce.CreateVerifier();
        var state = _stateProtector.Protect(new PlaudOAuthState(firmUser.Id, verifier, DateTime.UtcNow));
        try
        {
            return Redirect(await _plaud.BuildAuthorizationUrlAsync(state, verifier, ct));
        }
        catch (Exception ex)
        {
            _logger.LogError("[PlaudOAuth] authorize failed for user {UserId}: {ErrorType}", firmUser.Id, ex.GetType().Name);
            return Redirect("/meetings?plaudConnectError=1");
        }
    }

    // Anonymous: Plaud's cross-site redirect may not carry the FIRM cookie (SameSite). Identity comes
    // solely from the protected state, which is also the CSRF protection.
    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!_plaud.IsEnabled)
            return NotFound();

        var parsed = _stateProtector.Unprotect<PlaudOAuthState>(state);
        if (parsed == null)
        {
            _logger.LogWarning("[PlaudOAuth] callback rejected: state invalid or expired");
            return BadRequest("Invalid or expired state");
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            // The user declined consent or Plaud returned an error; error_description is not logged.
            _logger.LogWarning("[PlaudOAuth] callback for user {UserId} without authorization (denied={Denied})",
                parsed.UserId, !string.IsNullOrEmpty(error));
            return Redirect("/meetings?plaudConnectError=1");
        }

        try
        {
            await _plaud.HandleCallbackAsync(parsed.UserId, code, parsed.Verifier, ct);
            // WI #9135 — D13 auto-sync on by default on first connect; a reconnect resumes polling.
            // Best effort: the account is linked either way.
            try
            {
                await _sync.OnAccountConnectedAsync(parsed.UserId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[PlaudOAuth] sync settings after connect failed for user {UserId}: {ErrorType}",
                    parsed.UserId, ex.GetType().Name);
            }
            return Redirect("/meetings?plaudConnected=1");
        }
        catch (Exception ex)
        {
            _logger.LogError("[PlaudOAuth] callback failed for user {UserId}: {ErrorType}", parsed.UserId, ex.GetType().Name);
            return Redirect("/meetings?plaudConnectError=1");
        }
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        if (!_plaud.IsEnabled)
            return NotFound();

        var firmUser = await ResolveCurrentUserAsync();
        if (firmUser == null)
            return Unauthorized();

        await _plaud.DisconnectAsync(firmUser.Id, ct);
        // WI #9135 — state = disconnected (polling already stops: the due query joins the account row).
        try
        {
            await _sync.OnAccountDisconnectedAsync(firmUser.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[PlaudOAuth] sync state after disconnect failed for user {UserId}: {ErrorType}",
                firmUser.Id, ex.GetType().Name);
        }
        return Redirect("/meetings?plaudDisconnected=1");
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

    /// <summary>Sealed in the state: who is connecting, their PKCE verifier, and when.</summary>
    private record PlaudOAuthState(Guid UserId, string Verifier, DateTime IssuedAtUtc) : IOAuthStatePayload;
}
