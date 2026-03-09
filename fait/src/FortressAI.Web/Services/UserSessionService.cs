using FortressAI.Shared.Models;

namespace FortressAI.Web.Services;

/// <summary>
/// Scoped service that holds the current user session for Blazor Server circuits.
/// In Blazor Server, each circuit gets its own scoped service instance.
/// </summary>
public class UserSessionService
{
    public AppUser? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser != null;
    public bool IsAdmin => CurrentUser?.Role == "admin";
    public Guid UserId => CurrentUser?.Id ?? Guid.Empty;

    public event Action? OnAuthStateChanged;

    public void SetUser(AppUser? user)
    {
        CurrentUser = user;
        OnAuthStateChanged?.Invoke();
    }

    public void Logout()
    {
        CurrentUser = null;
        OnAuthStateChanged?.Invoke();
    }
}
