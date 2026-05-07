using Microsoft.EntityFrameworkCore;
using FortressAI.V2.Web.Data;

namespace FortressAI.V2.Web.Services;

public class FipTokenProvider : IFipTokenProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDbContextFactory<FipPortalDbContext> _fipPortalDbFactory;

    public FipTokenProvider(IHttpContextAccessor httpContextAccessor, IDbContextFactory<FipPortalDbContext> fipPortalDbFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _fipPortalDbFactory = fipPortalDbFactory;
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return null;

        var entraOid = user.FindFirst("oid")?.Value
                    ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(entraOid)) return null;

        await using var db = await _fipPortalDbFactory.CreateDbContextAsync();
        var tokenRecord = await db.UserMicrosoftTokens.FindAsync(entraOid);

        if (tokenRecord == null) return null;

        // If expired (or expiring within 5 minutes), degrade gracefully
        if (tokenRecord.ExpiresAt < DateTime.UtcNow.AddMinutes(5))
            return null;

        return tokenRecord.AccessToken;
    }
}
