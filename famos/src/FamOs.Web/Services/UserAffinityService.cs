using Microsoft.Extensions.Options;

namespace FamOs.Web.Services;

/// <summary>
/// Resolves the affinity group ID for the currently logged-in user.
///
/// Resolution order:
/// 1. Entra custom claim "affinity_id" on the access token
/// 2. UserAffinityMap in appsettings (email → affinityId)
/// 3. Single AffinityConfig.AffinityId fallback (backward compat)
/// </summary>
public class UserAffinityService
{
    private readonly UserSessionService _session;
    private readonly AffinityConfig     _config;

    public UserAffinityService(UserSessionService session, IOptions<AffinityConfig> config)
    {
        _session = session;
        _config  = config.Value;
    }

    public async Task<string> GetCurrentAffinityIdAsync()
    {
        var user = await _session.GetUserAsync();

        // 1. Entra custom claim (requires Entra app manifest to include affinity_id)
        var claimValue = user.FindFirst("affinity_id")?.Value;
        if (!string.IsNullOrEmpty(claimValue))
            return claimValue;

        // 2. Email-based map in appsettings
        var email = await _session.GetUserEmailAsync();
        if (!string.IsNullOrEmpty(email)
            && _config.UserAffinityMap.TryGetValue(email, out var mapped))
            return mapped;

        // 3. Fallback to single-tenant AffinityId
        return _config.AffinityId;
    }

    /// <summary>Returns the full AffinityGroupConfig for the current user's affinity.</summary>
    public async Task<AffinityGroupConfig?> GetCurrentAffinityConfigAsync()
    {
        var affinityId = await GetCurrentAffinityIdAsync();
        return _config.AffinityGroups.FirstOrDefault(g => g.AffinityId == affinityId)
            ?? new AffinityGroupConfig
            {
                AffinityId  = _config.AffinityId,
                DisplayName = _config.DisplayName,
                PortalName  = _config.PortalName,
                LogoPath    = _config.LogoPath,
            };
    }
}
