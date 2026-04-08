using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace FortressNexus.Web.Services;

public class UserContextService
{
    private readonly AuthenticationStateProvider _authStateProvider;

    public UserContextService(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    public async Task<string> GetUpnAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        return authState.User.FindFirst("preferred_username")?.Value
            ?? authState.User.FindFirst(ClaimTypes.Email)?.Value
            ?? authState.User.FindFirst(ClaimTypes.Name)?.Value
            ?? "unknown";
    }

    public async Task<bool> IsReviewerAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        return authState.User.IsInRole(NexusRoles.Reviewer);
    }

    public async Task<bool> IsAdminAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        return authState.User.IsInRole(NexusRoles.Admin);
    }
}
