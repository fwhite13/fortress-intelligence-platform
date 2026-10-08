using System.Security.Claims;
using FortressIntelligenceRM.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// WI #8034 — single admin check for the /admin area. Same rules as the org-context page/controller:
/// DB firm_users.is_admin first, then the Firm:AdminEntraOid bootstrap allowlist, then role claims.
/// </summary>
public class AdminAccessService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IConfiguration _config;

    public AdminAccessService(IDbContextFactory<FirmDbContext> dbFactory, IConfiguration config)
    {
        _dbFactory = dbFactory;
        _config = config;
    }

    public async Task<bool> IsAdminAsync(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return false;

        var userOid = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        if (!string.IsNullOrEmpty(userOid))
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            if (await db.Users.AnyAsync(u => u.EntraOid == userOid && u.IsAdmin))
                return true;

            var adminOids = (_config["Firm:AdminEntraOid"] ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (adminOids.Any(oid => string.Equals(oid, userOid, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return user.IsInRole("admin") || user.IsInRole("Admin") || user.HasClaim("roles", "admin");
    }
}
