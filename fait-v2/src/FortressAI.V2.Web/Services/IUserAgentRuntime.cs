using System.Runtime.CompilerServices;

namespace FortressAI.V2.Web.Services;

public interface IUserAgentRuntime
{
    /// <summary>Ensure a Fargate task is running for the user. Idempotent.</summary>
    Task<RuntimeSession> EnsureRunningAsync(string userId, CancellationToken ct = default);

    /// <summary>Stop the user's Fargate task. Idempotent.</summary>
    Task StopAsync(string userId, CancellationToken ct = default);

    /// <summary>Get current session state for user.</summary>
    Task<RuntimeSession?> GetSessionAsync(string userId, CancellationToken ct = default);

    /// <summary>Check if user's task is healthy and responsive.</summary>
    Task<bool> IsHealthyAsync(string userId, CancellationToken ct = default);

    /// <summary>Send a turn to the user's Fargate task and stream the response.</summary>
    IAsyncEnumerable<HarnessEvent> SendTurnAsync(string userId, TurnRequest request, CancellationToken ct = default);
}

public record RuntimeSession(
    string UserId,
    string TaskArn,
    string PrivateIp,
    int Port,
    RuntimeSessionStatus Status,
    DateTimeOffset StartedAt,
    string? SessionId
);

public enum RuntimeSessionStatus
{
    Starting,
    Running,
    Stopping,
    Stopped,
    Unknown
}

public record TurnRequest(
    string Message,
    string? SystemPrompt = null,
    string? SessionId = null
);

public record HarnessEvent(
    string Type,         // "text" | "log" | "done" | "error"
    string? Content = null,
    int? ExitCode = null,
    string? ErrorMessage = null
);
