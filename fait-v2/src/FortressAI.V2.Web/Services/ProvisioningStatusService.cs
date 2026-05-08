using FortressAI.V2.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class ProvisioningStatusService : IProvisioningStatusService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ProvisioningStatusService> _logger;

    public ProvisioningStatusService(
        IDbContextFactory<FaitV2DbContext> dbFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ProvisioningStatusService> logger)
    {
        _dbFactory = dbFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<bool> CheckReadyAsync(CancellationToken ct = default)
    {
        try
        {
            var httpCtx = _httpContextAccessor.HttpContext;
            var entraOid = httpCtx?.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                        ?? httpCtx?.User.FindFirst("oid")?.Value;

            if (string.IsNullOrEmpty(entraOid))
            {
                // No user context — return true, let auth/routing handle it
                return true;
            }

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var user = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.EntraOid == entraOid, ct);

            return user?.OnboardingCompletedAt != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProvisioningStatusService.CheckReadyAsync failed — defaulting to ready=true");
            return true; // fail-open: let routing handle redirect
        }
    }
}
