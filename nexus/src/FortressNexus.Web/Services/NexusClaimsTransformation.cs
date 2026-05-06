using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using FortressNexus.Web.Data;

namespace FortressNexus.Web.Services;

public class NexusClaimsTransformation : IClaimsTransformation
{
    private readonly IDbContextFactory<NexusDbContext> _dbFactory;

    public NexusClaimsTransformation(IDbContextFactory<NexusDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        // Get UPN from preferred_username (Entra) or fallback claims
        var upn = principal.FindFirst("preferred_username")?.Value
               ?? principal.FindFirst(ClaimTypes.Email)?.Value
               ?? principal.FindFirst(ClaimTypes.Upn)?.Value;

        if (string.IsNullOrEmpty(upn))
            return principal;

        // Skip if NexusAdmin or NexusReviewer role already injected (avoid duplicate on re-auth)
        if (principal.HasClaim(c => c.Type == ClaimTypes.Role &&
            (c.Value == NexusRoles.Admin || c.Value == NexusRoles.Reviewer || c.Value == NexusRoles.User)))
            return principal;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var roles = await db.NexusUserRoles
            .Where(r => r.UserUpn == upn)
            .Select(r => r.Role)
            .ToListAsync();

        if (roles.Count == 0)
            return principal;

        var clone = principal.Clone();
        var identity = clone.Identity as ClaimsIdentity
            ?? throw new InvalidOperationException("ClaimsIdentity not found on cloned principal — cannot inject NEXUS roles.");
        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

        return clone;
    }
}
