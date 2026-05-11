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

            if (runResp.Tasks.Count == 0)
            {
                _logger.LogError("RunTask returned no tasks and no failures for user {UserId} — possible throttle or transient error", userId);
                throw new InvalidOperationException("ECS RunTask returned no tasks and no failures — possible throttle or transient error, retry recommended.");
            }

            var newEcsTask = runResp.Tasks.FirstOrDefault();
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

        // Read SSE stream line by line — ADO#3241: support typed SSE events (event: + data:)
        using var stream = await response!.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? pendingEventType = null;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            // Blank line = SSE event boundary
            if (string.IsNullOrWhiteSpace(line))
            {
                pendingEventType = null;
                continue;
            }

            if (line.StartsWith(':'))
                continue;

            // Track named event type
            if (line.StartsWith("event: "))
            {
                pendingEventType = line["event: ".Length..].Trim();
                continue;
            }

            if (!line.StartsWith("data: "))
            {
                _logger.LogWarning("SendTurnAsync: unexpected SSE line: {Line}", line);
                continue;
            }

            var json = line["data: ".Length..];
            HarnessEvent? evt = null;

            if (pendingEventType is "kb_sources" or "tool_call")
            {
                // Typed SSE events — wrap as HarnessEvent with type from event line and payload from data
                evt = new HarnessEvent(pendingEventType, Payload: json);
                pendingEventType = null;
            }
            else
            {
                // Standard JSON-encoded HarnessEvent
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
