using System.Security.Claims;
using FortressAI.Web.Data;
using FortressAI.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Controllers;

/// <summary>
/// REST endpoints for the FAIT for Excel add-in.
/// GET /api/excel/whoami — resolve FAIT identity for an Entra-authenticated user.
/// </summary>
[ApiController]
[Route("api/excel")]
public class ExcelAddinController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ExcelAddinController> _logger;

    public ExcelAddinController(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<ExcelAddinController> logger)
    {
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    /// <summary>
    /// Resolve or provision the FAIT AppUser for the calling Entra user.
    /// Called by the taskpane after first sign-in to get the FAIT userId.
    /// </summary>
    [HttpGet("whoami")]
    [Authorize(AuthenticationSchemes = "EntraBearer")]
    public async Task<IActionResult> WhoAmI()
    {
        var email = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst(ClaimTypes.Email)?.Value ?? "";
        var name  = User.FindFirst("name")?.Value
                    ?? User.FindFirst(ClaimTypes.Name)?.Value ?? email;

        if (string.IsNullOrEmpty(email))
            return Unauthorized(new { error = "No email claim in token" });

        await using var db = await _dbFactory.CreateDbContextAsync();

        var oidClaim = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        // Look up existing Entra user by email
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.IsEntraUser && u.Email == email);

        if (user == null)
        {
            // Provision new FAIT user for this Entra identity (first login)
            user = new AppUser
            {
                Id          = Guid.NewGuid(),
                Email       = email,
                DisplayName = name,
                IsEntraUser = true,
                IsActive    = true,
                Role        = "user",
                CreatedAt   = DateTime.UtcNow,
                EntraOid    = oidClaim,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            _logger.LogInformation(
                "ExcelAddin: Provisioned new Entra user {Email} as FAIT user {Id}",
                email, user.Id);
        }

        // Backfill EntraOid for existing users who were provisioned before ADO#1240
        if (user.EntraOid == null && oidClaim != null)
        {
            user.EntraOid = oidClaim;
            await db.SaveChangesAsync();
            _logger.LogInformation("ExcelAddin: Backfilled EntraOid for user {Id}", user.Id);
        }

        return Ok(new
        {
            userId     = user.Id,
            email      = user.Email,
            name       = user.DisplayName ?? user.Email,
            authScheme = User.Identity?.AuthenticationType ?? "unknown",
        });
    }
}
