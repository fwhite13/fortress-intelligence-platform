using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace FortressAI.V2.Web.Services;

/// <summary>
/// Drop-in replacement for FargateUserAgentRuntime that calls Bedrock ConverseStreamAsync directly.
/// No Fargate, no ECS. Conversation history is kept in-memory per userId (Option B — acceptable for demo).
/// </summary>
public class DirectBedrockUserAgentRuntime : IUserAgentRuntime
{
    private const string ModelId = "us.anthropic.claude-sonnet-4-5-20250929-v1:0";
    private const int MaxTokens = 4096;
    private const float Temperature = 0.7f;

    private readonly IAmazonBedrockRuntime _bedrock;
    private readonly ILogger<DirectBedrockUserAgentRuntime> _logger;

    // In-memory conversation history keyed by userId.
    // Static so history survives Scoped DI re-instantiation per request.
    // Acceptable for demo — no persistence across process restarts.
    private static readonly ConcurrentDictionary<string, List<ConversationEntry>> _history = new();
    private static readonly SemaphoreSlim _historyLock = new(1, 1);

    public DirectBedrockUserAgentRuntime(
        IAmazonBedrockRuntime bedrock,
        ILogger<DirectBedrockUserAgentRuntime> logger)
    {
        _bedrock = bedrock;
        _logger = logger;
    }

    // ── No-op methods — no Fargate lifecycle needed ─────────────────────────

    public Task<RuntimeSession> EnsureRunningAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(new RuntimeSession(userId, string.Empty, string.Empty, 0, RuntimeSessionStatus.Running, DateTimeOffset.UtcNow, null));

    public Task StopAsync(string userId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<RuntimeSession?> GetSessionAsync(string userId, CancellationToken ct = default)
        => Task.FromResult<RuntimeSession?>(new RuntimeSession(userId, string.Empty, string.Empty, 0, RuntimeSessionStatus.Running, DateTimeOffset.UtcNow, null));

    public Task<bool> IsHealthyAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<string> DispatchToolCallAsync(string userId, string toolName, Dictionary<string, object> args, CancellationToken ct = default)
        => Task.FromResult("{}");

    // ── Core streaming method ────────────────────────────────────────────────

    public async IAsyncEnumerable<HarnessEvent> SendTurnAsync(
        string userId,
        TurnRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Get or create history for this user
        var history = _history.GetOrAdd(userId, _ => new List<ConversationEntry>());

        // 2. Add user message to history
        await _historyLock.WaitAsync(ct);
        try
        {
            history.Add(new ConversationEntry("user", request.Message));
        }
        finally
        {
            _historyLock.Release();
        }

        // 3. Build Bedrock ConverseStream request from history
        var messages = history.Select(h => new Message
        {
            Role = h.Role == "user" ? ConversationRole.User : ConversationRole.Assistant,
            Content = [new ContentBlock { Text = h.Content }]
        }).ToList();

        var converseRequest = new ConverseStreamRequest
        {
            ModelId = ModelId,
            Messages = messages,
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = MaxTokens,
                Temperature = Temperature
            }
        };

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            converseRequest.System = [new SystemContentBlock { Text = request.SystemPrompt }];
        }

        // 4. Call Bedrock
        ConverseStreamResponse? response = null;
        Exception? callError = null;
        try
        {
            response = await _bedrock.ConverseStreamAsync(converseRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DirectBedrock] ConverseStreamAsync failed for user {UserId}", userId);
            callError = ex;
        }

        if (callError != null)
        {
            yield return new HarnessEvent("error", ErrorMessage: callError.Message);
            yield break;
        }

        // 5. Stream events back to caller
        var assistantText = new System.Text.StringBuilder();

        foreach (var ev in response!.Stream)
        {
            if (ct.IsCancellationRequested) yield break;

            if (ev is ContentBlockDeltaEvent delta && delta.Delta?.Text != null)
            {
                var chunk = delta.Delta.Text;
                assistantText.Append(chunk);
                yield return new HarnessEvent("text", Content: chunk);
            }
            else if (ev is MessageStopEvent)
            {
                break;
            }
        }

        // 6. Save assistant response to history (trim to last 40 messages = 20 turns)
        await _historyLock.WaitAsync(ct);
        try
        {
            if (assistantText.Length > 0)
            {
                history.Add(new ConversationEntry("assistant", assistantText.ToString()));
            }

            // Trim history to stay within token budget
            if (history.Count > 40)
            {
                var trimmed = history.TakeLast(40).ToList();
                history.Clear();
                history.AddRange(trimmed);
            }
        }
        finally
        {
            _historyLock.Release();
        }

        yield return new HarnessEvent("done");
    }
}

/// <summary>Simple in-memory conversation entry (role + content).</summary>
public record ConversationEntry(string Role, string Content);
