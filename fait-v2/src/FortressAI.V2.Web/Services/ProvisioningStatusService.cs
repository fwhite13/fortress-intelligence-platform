using FortressAI.V2.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class ProvisioningStatusService : IProvisioningStatusService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<ProvisioningStatusService> _logger;

    public ProvisioningStatusService(
        IDbContextFactory<FaitV2DbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<ProvisioningStatusService> logger)
    {
        _dbFactory = dbFactory;
        _authStateProvider = authStateProvider;
        _logger = logger;
    }

    public async Task<bool> CheckReadyAsync(CancellationToken ct = default)
    {
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            var entraOid = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                        ?? user.FindFirst("oid")?.Value;

            if (string.IsNullOrEmpty(entraOid))
            {
                // No authenticated user — return true, auth middleware handles redirect
                return true;
            }

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var dbUser = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.EntraOid == entraOid, ct);

            return dbUser?.OnboardingCompletedAt != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProvisioningStatusService.CheckReadyAsync failed — defaulting to ready=true");
            return true;
        }
    }
}
