using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FamOs.Web.Services;

public class UserSessionService
{
    private readonly AuthenticationStateProvider _authProvider;

    public UserSessionService(AuthenticationStateProvider authProvider)
    {
        _authProvider = authProvider;
    }

    private ClaimsPrincipal? _user;

    public async Task<ClaimsPrincipal> GetUserAsync()
    {
        if (_user != null) return _user;
        var state = await _authProvider.GetAuthenticationStateAsync();
        _user = state.User;
        return _user;
    }

    public async Task<string> GetUserIdAsync()
    {
        var user = await GetUserAsync();
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("oid")?.Value
            ?? "unknown";
    }

    public async Task<string> GetUserNameAsync()
    {
        var user = await GetUserAsync();
        return user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.Identity?.Name
            ?? "User";
    }

    public async Task<string> GetUserEmailAsync()
    {
        var user = await GetUserAsync();
        return user.FindFirst("email")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? "";
    }
}
