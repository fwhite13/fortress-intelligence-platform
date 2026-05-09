# CC Brief: ADO#3126 — Fargate Session Lifecycle (Backend) for v1 FAIT

## Context
You are working in the v1 FAIT codebase at `/home/fredw/projects/fip/fait/src/FortressAI.Web/`.

The v2 codebase has `IUserAgentRuntime` and `FargateUserAgentRuntime` services. Your job is to port them to v1, adapting namespaces and DB context. All files changed are in the v1 project only.

## Task A: Create IUserAgentRuntime.cs

**File to create:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/IUserAgentRuntime.cs`

Create this file with the following exact content (namespace changed to `FortressAI.Web.Services`):

```csharp
using System.Runtime.CompilerServices;

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
    bool KbWriteAllowed = true
);

public record ChatHistoryEntry(string Role, string Content);

public record HarnessEvent(
    string Type,         // "text" | "log" | "done" | "error"
    string? Content = null,
    int? ExitCode = null,
    string? ErrorMessage = null,
    int? InputTokens = null,
    int? OutputTokens = null
);
```

## Task B: Create UserSession entity model

**File to create:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Data/Models/UserSession.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.Web.Data.Models;

[Table("user_sessions")]
public class UserSession
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? TaskArn { get; set; }
    public string? PrivateIp { get; set; }
    public string? FargateStatus { get; set; }
    public string? FargateSessionId { get; set; }
    public string? TaskDefinitionRevision { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

## Task C: Add UserSessions DbSet to AppDbContext

**File to modify:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Data/AppDbContext.cs`

1. Add using at top of file (after existing usings):
   ```csharp
   using FortressAI.Web.Data.Models;
   ```

2. Add DbSet after the existing `ChatAttachments` DbSet:
   ```csharp
   public DbSet<UserSession> UserSessions => Set<UserSession>();
   ```

3. Add entity configuration at the END of `OnModelCreating` (before the closing brace), after the `ChatAttachment` entity config block:
   ```csharp
   modelBuilder.Entity<UserSession>(entity =>
   {
       entity.ToTable("user_sessions");
       entity.HasKey(e => e.Id);
       entity.Property(e => e.Id).ValueGeneratedNever();
       entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
       entity.Property(e => e.StartedAt).HasColumnName("started_at");
       entity.Property(e => e.LastActiveAt).HasColumnName("last_active_at");
       entity.Property(e => e.EndedAt).HasColumnName("ended_at");
       entity.Property(e => e.TaskArn).HasColumnName("task_arn").HasMaxLength(500);
       entity.Property(e => e.PrivateIp).HasColumnName("private_ip").HasMaxLength(45);
       entity.Property(e => e.FargateStatus).HasColumnName("fargate_status").HasMaxLength(20);
       entity.Property(e => e.FargateSessionId).HasColumnName("fargate_session_id").HasMaxLength(200);
       entity.Property(e => e.TaskDefinitionRevision).HasColumnName("task_definition_revision").HasMaxLength(100);
       entity.Property(e => e.CreatedAt).HasColumnName("created_at");
       entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
       entity.HasIndex(e => e.UserId).HasDatabaseName("ix_user_sessions_user_id");
       entity.HasIndex(e => e.LastActiveAt).HasDatabaseName("ix_user_sessions_last_active_at");
   });
   ```

## Task D: Create FargateUserAgentRuntime.cs

**File to create:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/FargateUserAgentRuntime.cs`

This is a full port from v2. Key adaptations:
- Namespace: `FortressAI.Web.Services`
- DB context: `IDbContextFactory<AppDbContext>` (not `FaitV2DbContext`)
- DB model: `UserSession` from `FortressAI.Web.Data.Models` (not v2's model)
- Remove `GetUserS3PrefixAsync` call to `MainAssistants` — v1 doesn't have that DbSet. Replace with simple `$"workspaces/{userId}/"` directly.
- All using statements use `FortressAI.Web.*` not `FortressAI.V2.Web.*`

Create the file with this content:

```csharp
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Amazon.ECS;
using Amazon.ECS.Model;
using FortressAI.Web.Data;
using FortressAI.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Services;

public class FargateUserAgentRuntime : IUserAgentRuntime
{
    private readonly IAmazonECS _ecs;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FargateUserAgentRuntime> _logger;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _launchLocks = new();

    // Config helpers
    private string ClusterArn => _config["Fargate:ClusterArn"] ?? throw new InvalidOperationException("Fargate:ClusterArn not configured");
    private string TaskDefinition => _config["Fargate:TaskDefinition"] ?? "fait-agent-harness:1";
    private string[] SubnetIds => (_config["Fargate:SubnetIds"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
    private string[] SecurityGroupIds => (_config["Fargate:SecurityGroupIds"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
    private string ContainerName => _config["Fargate:ContainerName"] ?? "harness";
    private int HarnessPort => int.TryParse(_config["Fargate:HarnessPort"], out var p) ? p : 3000;

    public FargateUserAgentRuntime(
        IAmazonECS ecs,
        IDbContextFactory<AppDbContext> dbFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<FargateUserAgentRuntime> logger)
    {
        _ecs = ecs;
        _dbFactory = dbFactory;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    // ─── EnsureRunningAsync ────────────────────────────────────────────────────

    public async Task<RuntimeSession> EnsureRunningAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // 1. Check for existing active session
        var existing = await db.UserSessions
            .Where(s => s.UserId == userId
                     && s.TaskArn != null
                     && s.EndedAt == null
                     && (s.FargateStatus == "Running" || s.FargateStatus == "Starting"))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (existing?.TaskArn != null)
        {
            // Verify task is still actually running in ECS
            var describeResp = await _ecs.DescribeTasksAsync(new DescribeTasksRequest
            {
                Cluster = ClusterArn,
                Tasks = [existing.TaskArn]
            }, ct);

            var ecsTask = describeResp.Tasks.FirstOrDefault();
            if (ecsTask?.LastStatus == "RUNNING")
            {
                // Health-check the actual harness process
                var harnessReachable = false;
                try
                {
                    using var healthCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    healthCts.CancelAfter(TimeSpan.FromSeconds(3));
                    var healthClient = _httpClientFactory.CreateClient("HarnessClient");
                    var healthResp = await healthClient.GetAsync(
                        $"http://{existing.PrivateIp}:{HarnessPort}/health",
                        healthCts.Token);
                    harnessReachable = healthResp.IsSuccessStatusCode;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Harness health check failed for user {UserId} at {Ip}:{Port} — invalidating session",
                        userId, existing.PrivateIp, HarnessPort);
                }

                if (harnessReachable)
                {
                    // Version check — stop and replace if task def revision changed
                    var currentRevision = TaskDefinition;
                    if (!string.IsNullOrEmpty(existing.TaskDefinitionRevision)
                        && existing.TaskDefinitionRevision != currentRevision)
                    {
                        _logger.LogInformation(
                            "EnsureRunningAsync: task def revision changed ({OldRev} → {NewRev}) for user {UserId} — replacing",
                            existing.TaskDefinitionRevision, currentRevision, userId);
                        try
                        {
                            await _ecs.StopTaskAsync(new StopTaskRequest
                            {
                                Cluster = ClusterArn,
                                Task = existing.TaskArn,
                                Reason = $"Task def revision changed: {existing.TaskDefinitionRevision} → {currentRevision}"
                            }, ct);
                        }
                        catch (Exception stopEx)
                        {
                            _logger.LogWarning(stopEx, "EnsureRunningAsync: failed to stop stale revision task {TaskArn}", existing.TaskArn);
                        }
                        existing.FargateStatus = "Stopped";
                        existing.EndedAt = DateTime.UtcNow;
                        existing.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                        // Fall through to launch new task
                    }
                    else
                    {
                        _logger.LogDebug("Returning existing running Fargate task for user {UserId}: {TaskArn}", userId, existing.TaskArn);
                        return MapToRuntimeSession(existing);
                    }
                }
                else
                {
                    // Health check failed — invalidate stale session
                    _logger.LogWarning("Cached session for user {UserId} failed health check — invalidating and launching new task", userId);
                    try
                    {
                        await _ecs.StopTaskAsync(new StopTaskRequest
                        {
                            Cluster = ClusterArn,
                            Task = existing.TaskArn,
                            Reason = "Stale session — harness health check failed, replacing"
                        }, ct);
                    }
                    catch (Exception stopEx)
                    {
                        _logger.LogWarning(stopEx, "EnsureRunningAsync: failed to stop stale task {TaskArn} — continuing", existing.TaskArn);
                    }
                    existing.FargateStatus = "Stopped";
                    existing.EndedAt = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            else
            {
                // Task no longer running in ECS — mark ended
                try
                {
                    await _ecs.StopTaskAsync(new StopTaskRequest
                    {
                        Cluster = ClusterArn,
                        Task = existing.TaskArn,
                        Reason = "Stale session — ECS task no longer RUNNING, replacing"
                    }, ct);
                }
                catch (Exception stopEx)
                {
                    _logger.LogWarning(stopEx, "EnsureRunningAsync: failed to stop stale task {TaskArn} — continuing", existing.TaskArn);
                }
                existing.FargateStatus = "Stopped";
                existing.EndedAt = DateTime.UtcNow;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        // 2. Launch a new Fargate task (per-user mutex prevents double-spawn)
        var launchLock = _launchLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await launchLock.WaitAsync(ct);
        try
        {
            // Re-check: another concurrent request may have already launched
            await using var db2 = await _dbFactory.CreateDbContextAsync(ct);
            var recheck = await db2.UserSessions
                .Where(s => s.UserId == userId
                         && s.TaskArn != null
                         && s.EndedAt == null
                         && (s.FargateStatus == "Running" || s.FargateStatus == "Starting"))
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync(ct);

            if (recheck != null)
            {
                _logger.LogInformation("EnsureRunningAsync: concurrent launch already completed for user {UserId}", userId);
                return MapToRuntimeSession(recheck);
            }

            _logger.LogInformation("Launching new Fargate task for user {UserId}", userId);

            var runResp = await _ecs.RunTaskAsync(new RunTaskRequest
            {
                Cluster = ClusterArn,
                TaskDefinition = TaskDefinition,
                LaunchType = LaunchType.FARGATE,
                NetworkConfiguration = new NetworkConfiguration
                {
                    AwsvpcConfiguration = new AwsVpcConfiguration
                    {
                        Subnets = [.. SubnetIds],
                        SecurityGroups = [.. SecurityGroupIds],
                        AssignPublicIp = AssignPublicIp.ENABLED
                    }
                },
                Overrides = new TaskOverride
                {
                    ContainerOverrides =
                    [
                        new ContainerOverride
                        {
                            Name = ContainerName,
                            Environment =
                            [
                                new Amazon.ECS.Model.KeyValuePair { Name = "FAIT_USER_ID",          Value = userId },
                                new Amazon.ECS.Model.KeyValuePair { Name = "WORKSPACE_DIR",         Value = $"/workspace/{userId}" },
                                new Amazon.ECS.Model.KeyValuePair { Name = "WORKSPACE_S3_PREFIX",   Value = $"workspaces/{userId}/" },
                                new Amazon.ECS.Model.KeyValuePair { Name = "WORKSPACE_S3_BUCKET",   Value = _config["AWS:WorkspaceBucket"] ?? "fortress-user-workspaces" }
                            ]
                        }
                    ]
                }
            }, ct);

            if (runResp.Failures.Count > 0)
            {
                var reason = runResp.Failures[0].Reason;
                _logger.LogError("RunTask failed for user {UserId}: {Reason}", userId, reason);
                throw new InvalidOperationException($"Failed to start Fargate task: {reason}");
            }

            var newEcsTask = runResp.Tasks[0];
            var taskArn = newEcsTask.TaskArn ?? string.Empty;

            // 3. Create DB record with Starting status
            var sessionId = Guid.NewGuid().ToString();
            var session = new UserSession
            {
                Id = sessionId,
                UserId = userId,
                TaskArn = taskArn,
                FargateStatus = "Starting",
                FargateSessionId = sessionId,
                TaskDefinitionRevision = TaskDefinition,
                StartedAt = DateTime.UtcNow,
                LastActiveAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.UserSessions.Add(session);
            await db.SaveChangesAsync(ct);

            // 4. Poll until RUNNING (max 90s, every 3s)
            const int MaxPolls = 30;
            const int PollDelayMs = 3000;

            for (int i = 0; i < MaxPolls; i++)
            {
                await System.Threading.Tasks.Task.Delay(PollDelayMs, ct);

                var pollResp = await _ecs.DescribeTasksAsync(new DescribeTasksRequest
                {
                    Cluster = ClusterArn,
                    Tasks = [taskArn]
                }, ct);

                var polledTask = pollResp.Tasks.FirstOrDefault();
                if (polledTask == null)
                {
                    _logger.LogWarning("DescribeTasks returned empty for {TaskArn}, poll {Poll}", taskArn, i + 1);
                    continue;
                }

                _logger.LogDebug("Task {TaskArn} status: {Status} (poll {Poll}/{Max})", taskArn, polledTask.LastStatus, i + 1, MaxPolls);

                if (polledTask.LastStatus == "RUNNING")
                {
                    var privateIp = GetPrivateIpFromTask(polledTask);

                    session.PrivateIp = privateIp;
                    session.FargateStatus = "Running";
                    session.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);

                    _logger.LogInformation("Fargate task RUNNING for user {UserId}: {TaskArn} @ {Ip}", userId, taskArn, privateIp);
                    return MapToRuntimeSession(session);
                }

                if (polledTask.LastStatus is "STOPPED" or "DEPROVISIONING")
                {
                    var reason = polledTask.StoppedReason ?? "Unknown";
                    session.FargateStatus = "Stopped";
                    session.EndedAt = DateTime.UtcNow;
                    session.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    throw new InvalidOperationException($"Fargate task stopped unexpectedly: {reason}");
                }
            }

            // Timeout
            session.FargateStatus = "Stopped";
            session.EndedAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            throw new TimeoutException($"Fargate task for user {userId} did not reach RUNNING state within 90 seconds.");
        }
        finally
        {
            launchLock.Release();
        }
    }

    // ─── StopAsync ────────────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task StopAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var session = await db.UserSessions
            .Where(s => s.UserId == userId
                     && s.TaskArn != null
                     && s.EndedAt == null
                     && (s.FargateStatus == "Running" || s.FargateStatus == "Starting"))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (session?.TaskArn == null)
        {
            _logger.LogDebug("StopAsync: no active session found for user {UserId} — idempotent no-op", userId);
            return;
        }

        try
        {
            await _ecs.StopTaskAsync(new StopTaskRequest
            {
                Cluster = ClusterArn,
                Task = session.TaskArn,
                Reason = "User requested stop"
            }, ct);

            _logger.LogInformation("Stopped Fargate task {TaskArn} for user {UserId}", session.TaskArn, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ECS StopTask failed for {TaskArn} — marking stopped anyway", session.TaskArn);
        }

        session.FargateStatus = "Stopped";
        session.EndedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ─── GetSessionAsync ──────────────────────────────────────────────────────

    public async Task<RuntimeSession?> GetSessionAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var session = await db.UserSessions
            .Where(s => s.UserId == userId && s.TaskArn != null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        return session == null ? null : MapToRuntimeSession(session);
    }

    // ─── IsHealthyAsync ───────────────────────────────────────────────────────

    public async Task<bool> IsHealthyAsync(string userId, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(userId, ct);
        if (session == null || session.Status == RuntimeSessionStatus.Stopped || string.IsNullOrEmpty(session.PrivateIp))
            return false;

        try
        {
            var client = _httpClientFactory.CreateClient("HarnessClient");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
            var resp = await client.GetAsync($"http://{session.PrivateIp}:{session.Port}/health", linkedCts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed for user {UserId} at {Ip}:{Port}", userId, session.PrivateIp, session.Port);
            return false;
        }
    }

    // ─── SendTurnAsync ────────────────────────────────────────────────────────

    public async IAsyncEnumerable<HarnessEvent> SendTurnAsync(
        string userId,
        TurnRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("SendTurnAsync: calling EnsureRunningAsync for userId={UserId}", userId);
        RuntimeSession session = await EnsureRunningAsync(userId, ct);

        var url = $"http://{session.PrivateIp}:{session.Port}/turn";
        _logger.LogInformation("SendTurnAsync: preparing POST to {Url} for userId={UserId}", url, userId);

        var client = _httpClientFactory.CreateClient("HarnessClient");

        HttpResponseMessage? response = null;
        Exception? postError = null;
        try
        {
            var jsonContent = System.Net.Http.Json.JsonContent.Create(request);
            var httpReq = new HttpRequestMessage(HttpMethod.Post, url) { Content = jsonContent };
            response = await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (IsConnectionRefused(ex))
        {
            _logger.LogWarning("Harness at {Ip}:{Port} refused connection for user {UserId} — invalidating session and retrying",
                session.PrivateIp, session.Port, userId);

            // Invalidate the stale session row
            try
            {
                await using var invalidateDb = await _dbFactory.CreateDbContextAsync(ct);
                var staleSession = await invalidateDb.UserSessions
                    .Where(s => s.UserId == userId
                             && s.EndedAt == null
                             && (s.FargateStatus == "Running" || s.FargateStatus == "Starting"))
                    .OrderByDescending(s => s.StartedAt)
                    .FirstOrDefaultAsync(ct);
                if (staleSession != null)
                {
                    staleSession.FargateStatus = "Stopped";
                    staleSession.EndedAt = DateTime.UtcNow;
                    staleSession.UpdatedAt = DateTime.UtcNow;
                    await invalidateDb.SaveChangesAsync(ct);
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogWarning(dbEx, "Failed to invalidate stale session for user {UserId}", userId);
            }

            // Explicitly stop stale ECS task and re-launch
            try
            {
                if (session.TaskArn != null)
                {
                    try
                    {
                        await _ecs.StopTaskAsync(new StopTaskRequest
                        {
                            Cluster = ClusterArn,
                            Task = session.TaskArn,
                            Reason = "Connection refused — stopping stale task before relaunch"
                        }, ct);
                    }
                    catch (Exception stopEx)
                    {
                        _logger.LogWarning(stopEx, "SendTurnAsync: failed to stop stale task {TaskArn}", session.TaskArn);
                    }
                }
                session = await EnsureRunningAsync(userId, ct);
                url = $"http://{session.PrivateIp}:{session.Port}/turn";
                var retryContent = System.Net.Http.Json.JsonContent.Create(request);
                var retryReq = new HttpRequestMessage(HttpMethod.Post, url) { Content = retryContent };
                response = await client.SendAsync(retryReq, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx, "Retry POST /turn failed for user {UserId} after session refresh", userId);
                postError = retryEx;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to POST /turn to harness for user {UserId} at {Url}", userId, url);
            postError = ex;
        }

        if (postError != null)
        {
            yield return new HarnessEvent("error", ErrorMessage: postError.Message);
            yield break;
        }

        // Read SSE stream line by line
        using var stream = await response!.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(':'))
                continue;

            if (!line.StartsWith("data: "))
            {
                _logger.LogWarning("SendTurnAsync: unexpected SSE line: {Line}", line);
                continue;
            }

            var json = line["data: ".Length..];
            HarnessEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<HarnessEvent>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse SSE data line: {Line}", line);
                continue;
            }

            if (evt == null) continue;

            yield return evt;

            if (evt.Type is "done" or "error")
                yield break;
        }
    }

    // ─── DispatchToolCallAsync ────────────────────────────────────────────────

    public async Task<string> DispatchToolCallAsync(string userId, string toolName, Dictionary<string, object> args, CancellationToken ct = default)
    {
        var harness = await EnsureRunningAsync(userId, ct);
        var client = _httpClientFactory.CreateClient("HarnessClient");
        var response = await client.PostAsJsonAsync($"http://{harness.PrivateIp}:{harness.Port}/tools/{toolName}", args, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private RuntimeSession MapToRuntimeSession(UserSession s)
    {
        var status = s.FargateStatus switch
        {
            "Starting"  => RuntimeSessionStatus.Starting,
            "Running"   => RuntimeSessionStatus.Running,
            "Stopping"  => RuntimeSessionStatus.Stopping,
            "Stopped"   => RuntimeSessionStatus.Stopped,
            _           => RuntimeSessionStatus.Unknown
        };

        return new RuntimeSession(
            UserId:    s.UserId,
            TaskArn:   s.TaskArn ?? string.Empty,
            PrivateIp: s.PrivateIp ?? string.Empty,
            Port:      HarnessPort,
            Status:    status,
            StartedAt: new DateTimeOffset(s.StartedAt, TimeSpan.Zero),
            SessionId: s.FargateSessionId
        );
    }

    private static bool IsConnectionRefused(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            if (current is SocketException se && se.SocketErrorCode == SocketError.ConnectionRefused)
                return true;
            current = current.InnerException;
        }
        return false;
    }

    private static string GetPrivateIpFromTask(Amazon.ECS.Model.Task task)
    {
        var eni = task.Attachments
            .FirstOrDefault(a => a.Type == "ElasticNetworkInterface");

        var ip = eni?.Details
            .FirstOrDefault(d => d.Name == "privateIPv4Address")
            ?.Value;

        return ip ?? string.Empty;
    }
}
```

## Task E: Add DDL to DatabaseInitializationService.cs

**File to modify:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/DatabaseInitializationService.cs`

In the `extraTables` array, add a new entry at the END (after the `chat_attachments` entry, before the closing `};` of the array). Add a comma after the `chat_attachments` tuple and then add:

```csharp
                ,("user_sessions", @"CREATE TABLE IF NOT EXISTS user_sessions (
    id VARCHAR(36) NOT NULL PRIMARY KEY,
    user_id VARCHAR(36) NOT NULL,
    started_at DATETIME(6) NOT NULL,
    last_active_at DATETIME(6) NOT NULL,
    ended_at DATETIME(6) NULL,
    task_arn VARCHAR(500) NULL,
    private_ip VARCHAR(45) NULL,
    fargate_status VARCHAR(20) NULL,
    fargate_session_id VARCHAR(200) NULL,
    task_definition_revision VARCHAR(100) NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    INDEX ix_user_sessions_user_id (user_id),
    INDEX ix_user_sessions_last_active_at (last_active_at)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci")
```

In the `alterStatements` array, add these entries at the END (before the closing `};`):

```csharp
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_completed_at DATETIME(6) NULL",
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_step INT NULL",
```

IMPORTANT: The existing ALTER TABLE loop only catches MySqlException 1060, 1061, 1091. `ADD COLUMN IF NOT EXISTS` is MySQL 8+ syntax that should be idempotent on its own, but keep inside the try-catch anyway.

## Task F: Register services in Program.cs

**File to modify:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs`

1. Add `using Amazon.ECS;` to the using statements at the top if not already present.

2. Find the section where `AddHttpClient` is called (around line 97 — `builder.Services.AddHttpClient();`). AFTER the existing named HttpClient registrations (after the `"mcp-transport"` and `"graph"` clients, around line 290-295), add:

```csharp
// Named HttpClient for Fargate harness communication — long timeout for SSE streaming
builder.Services.AddHttpClient("HarnessClient", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
```

3. After the existing service registrations (after the MCP services block, around line 265-270), add:

```csharp
// Fargate agent runtime
builder.Services.AddAWSService<IAmazonECS>();
builder.Services.AddSingleton<IUserAgentRuntime, FargateUserAgentRuntime>();
```

Check: if `IAmazonECS` or `AddAWSService<IAmazonECS>` is already in Program.cs, skip adding it again and just add the singleton registration.

## Task G: Add /api/agent/status endpoint

**File to modify:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs`

Find where minimal API endpoints are mapped (the `app.Map*` calls). After the existing `app.MapGet` for `/api/tokens/{userId}` (around line 497), add:

```csharp
app.MapGet("/api/agent/status", async (IUserAgentRuntime runtime, System.Security.Claims.ClaimsPrincipal user) =>
{
    var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();
    var session = await runtime.GetSessionAsync(userId);
    return Results.Ok(new { status = session?.Status.ToString() ?? "Stopped" });
}).RequireAuthorization();
```

## Task H: Build verification

After making ALL changes above, run:
```bash
cd /home/fredw/projects/fip/fait/src/FortressAI.Web
dotnet build 2>&1
```

The build must produce 0 errors. If there are errors, fix them. Common issues to watch for:
- `UserSession` model may conflict with EF Core trying to create a migration snapshot — it won't because v1 uses raw DDL, but if EF tries to generate the table from the model AND DatabaseInitializationService creates it separately, that's OK (CREATE TABLE IF NOT EXISTS on both sides)
- `AddAWSService<IAmazonECS>()` requires the `AWSSDK.Extensions.NETCore.Setup` package — check if it's already referenced; if not, the pattern in Program.cs for other AWS services uses the same method, so it must be available
- If `Amazon.ECS` namespace isn't available, check the existing AWS packages in the .csproj

After successful build, report:
1. All files created/modified
2. Any build warnings relevant to the new code
3. The exact build output (0 errors confirmation)

## ADDENDUM: v1 AWS registration pattern (CRITICAL)

v1 does NOT use `AddAWSService<T>()`. It uses explicit singleton registration:
```csharp
builder.Services.AddSingleton<Amazon.ECS.IAmazonECS>(sp =>
    new Amazon.ECS.AmazonECSClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));
```

Use THIS pattern for IAmazonECS, NOT `builder.Services.AddAWSService<IAmazonECS>()`.

Also, `AWSSDK.ECS` is NOT in the .csproj. You MUST add it:
```xml
<PackageReference Include="AWSSDK.ECS" Version="3.7.*" />
```

Add this to `/home/fredw/projects/fip/fait/src/FortressAI.Web/FortressAI.Web.csproj` in the ItemGroup with the other AWSSDK packages.
