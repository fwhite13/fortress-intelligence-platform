using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FortressAI.Web.Services;

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

    /// <summary>Dispatch a named tool call to the user's Fargate harness (e.g. Stitch MCP tools).</summary>
    Task<string> DispatchToolCallAsync(string userId, string toolName, Dictionary<string, object> args, CancellationToken ct = default);

    /// <summary>POST JSON payload to a harness endpoint for the given user. ADO#3560.</summary>
    Task<HttpResponseMessage> PostToHarnessAsync(string userId, string path, object payload, CancellationToken ct = default);
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
    string UserId,
    string Message,
    string? SystemPrompt = null,
    string? SessionId = null,
    bool TaskMode = false,
    bool ForceTaskMode = false,
    List<ChatHistoryEntry>? History = null,
    string? PluginAgentId = null,
    string? UserEmail = null,
    bool IsScheduledTask = false,
    bool KbWriteAllowed = true,
    string? ConversationId = null,
    List<string>? EnabledMcpSlugs = null,
    KbFlags? KbFlags = null,
    string? Model = null,   // ADO#3395 — per-turn model override; null = use harness default
    string? PersistedWorkingFolderId = null   // ADO#4144 — conversation's persisted working folder; harness uses to skip picker
);

// ADO#3241 — KB flags passed to harness for harness-side KB retrieval
// ADO#3278 — Added PersonalKbUserId and TeamIds for data isolation (Issue B)
public record KbFlags(
    bool CorpKbEnabled = false,
    bool PersonalKbEnabled = false,
    bool TeamKbEnabled = false,
    string? PersonalKbUserId = null,   // user's GUID — used as ownerId metadata filter in harness
    List<int>? TeamIds = null          // list of team IDs the user has enabled — used as teamId metadata filter
);

public record ChatHistoryEntry(string Role, string Content);

public record HarnessEvent(
    [property: JsonPropertyName("type")] string Type,         // "text" | "log" | "done" | "error" | "mode_switch" | "artifact" | "kb_sources" | "tool_call" | "task_progress"
    [property: JsonPropertyName("content")] string? Content = null,
    [property: JsonPropertyName("exitCode")] int? ExitCode = null,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null,
    [property: JsonPropertyName("inputTokens")] int? InputTokens = null,
    [property: JsonPropertyName("outputTokens")] int? OutputTokens = null,
    [property: JsonPropertyName("payload")] string? Payload = null   // JSON payload for mode_switch and future event types
);

// ADO#3241 — KB sources SSE payload
public record KbSourcesPayload(
    [property: JsonPropertyName("sources")] List<KbSourceItem>? Sources,
    [property: JsonPropertyName("wasSearched")] bool WasSearched = false
);

public record KbSourceItem(
    [property: JsonPropertyName("kbId")] string? KbId,
    [property: JsonPropertyName("kbName")] string? KbName,
    [property: JsonPropertyName("sourceCount")] int SourceCount,
    [property: JsonPropertyName("chunks")] List<KbChunkItem>? Chunks
);

public record KbChunkItem(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("excerpt")] string? Excerpt
);

// ADO#3244 — Task Progress SSE payload (Feature 2.4)
public record TaskProgressPayload(
    [property: JsonPropertyName("step")] string? Step,            // "start" | "tool_use" | "tool_result" | "done" | "error"
    [property: JsonPropertyName("toolName")] string? ToolName,    // null for non-tool steps
    [property: JsonPropertyName("status")] string? Status,        // "starting" | "calling" | "done" | "error"
    [property: JsonPropertyName("message")] string? Message,      // human-readable summary
    [property: JsonPropertyName("chipIcon")] string? ChipIcon     // ADO#4809: icon hint for chip rendering
);

// ADO#3560 — folder_required SSE payload
public record FolderRequiredPayload(
    [property: JsonPropertyName("folders")] List<FolderInfo>? Folders,
    [property: JsonPropertyName("lastFolderId")] string? LastFolderId,
    [property: JsonPropertyName("conversationId")] string? ConversationId  // ADO#3923: per-turn conversationId for @key
);

public record FolderInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("lastUsedAt")] DateTime? LastUsedAt
);

// ADO#3241 — Tool call SSE payload
public record ToolCallPayload(
    [property: JsonPropertyName("server")] string? Server,
    [property: JsonPropertyName("toolName")] string? ToolName,
    [property: JsonPropertyName("status")] string? Status,    // "calling" | "done" | "error"
    [property: JsonPropertyName("summary")] string? Summary
);
