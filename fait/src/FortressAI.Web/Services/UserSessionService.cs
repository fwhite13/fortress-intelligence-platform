using FortressAI.Shared.Models;
using Microsoft.Extensions.Logging;

namespace FortressAI.Web.Services;

/// <summary>
/// Scoped service that holds the current user session for Blazor Server circuits.
/// In Blazor Server, each circuit gets its own scoped service instance.
/// </summary>
public class UserSessionService
{
    private readonly ILogger<UserSessionService> _logger;

    public UserSessionService(ILogger<UserSessionService> logger)
    {
        _logger = logger;
    }

    public AppUser? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser != null;
    public bool IsAdmin => CurrentUser?.Role == "admin";
    public Guid UserId => CurrentUser?.Id ?? Guid.Empty;

    public event Action? OnAuthStateChanged;

    public void SetUser(AppUser? user)
    {
        _logger.LogInformation("[UserSessionService] SetUser called: email={Email} userId={UserId} isNull={IsNull}",
            user?.Email, user?.Id, user == null);
        CurrentUser = user;
        _logger.LogInformation("[UserSessionService] OnAuthStateChanged firing — IsAuthenticated={IsAuthenticated}",
            IsAuthenticated);
        OnAuthStateChanged?.Invoke();
        _logger.LogInformation("[UserSessionService] OnAuthStateChanged fired");
    }

    public void Logout()
    {
        _logger.LogInformation("[UserSessionService] Logout called");
        CurrentUser = null;
        OnAuthStateChanged?.Invoke();
    }
}
