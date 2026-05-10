namespace FortressAI.Web.Services;

public interface ITaskNotificationService
{
    /// <summary>Best-effort. Never throws. Sends SignalR toast and/or email depending on flags.</summary>
    Task NotifyTaskCompletedAsync(Guid userId, string taskName, string? resultSummary, CancellationToken ct = default);
    Task NotifyTaskPermanentlyFailedAsync(Guid userId, string taskName, string errorMessage, CancellationToken ct = default);
}
