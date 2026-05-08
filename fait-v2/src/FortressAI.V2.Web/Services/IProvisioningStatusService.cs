namespace FortressAI.V2.Web.Services;

/// <summary>
/// Lightweight check: is this user provisioned and ready?
/// Used by AssistantLoadingState to determine if the dashboard can show.
/// Separate from IUserProvisioningService to avoid triggering heavy provisioning.
/// </summary>
public interface IProvisioningStatusService
{
    /// <summary>
    /// Returns true if the current user has OnboardingCompletedAt set.
    /// Returns true for anonymous/unauthenticated users too (Routes.razor handles redirect).
    /// Never throws — swallows DB errors and returns true to let routing handle it.
    /// </summary>
    Task<bool> CheckReadyAsync(CancellationToken ct = default);
}
