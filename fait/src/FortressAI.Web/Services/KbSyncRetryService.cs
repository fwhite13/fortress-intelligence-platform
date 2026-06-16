using FortressAI.Web.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Services;

public class KbSyncRetryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KbSyncRetryService> _logger;
    private volatile bool _retryNeeded;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private readonly System.Collections.Concurrent.ConcurrentQueue<(string jobId, DateTime startedAt)> _pendingJobs = new();

    public KbSyncRetryService(IServiceScopeFactory scopeFactory, ILogger<KbSyncRetryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Called by KbDocumentService when StartIngestionAsync gets a ConflictException.</summary>
    public void RequestRetry()
    {
        _retryNeeded = true;
        _logger.LogInformation("[KbSync] Retry requested — will attempt on next 30s cycle");
    }

    /// <summary>Enqueue a Bedrock ingestion job for status polling.</summary>
    public void EnqueueJobForPolling(string jobId, DateTime startedAt)
    {
        _pendingJobs.Enqueue((jobId, startedAt));
        _logger.LogInformation("[KbSync] Enqueued job {JobId} for polling (started {StartedAt:u})", jobId, startedAt);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(RetryInterval, stoppingToken);

            // ── Retry pending ingestion start ────────────────────────────────
            if (_retryNeeded)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var kbDocSvc = scope.ServiceProvider.GetRequiredService<KbDocumentService>();
                    // StartIngestionAsync internally calls EnqueueJobForPolling on success
                    await kbDocSvc.StartIngestionAsync(throwOnConflict: true);
                    _retryNeeded = false;
                    _logger.LogInformation("[KbSync] Retry succeeded — flag cleared");
                }
                catch (Amazon.BedrockAgent.Model.ConflictException)
                {
                    _logger.LogInformation("[KbSync] Still busy — will retry in {Seconds}s", RetryInterval.TotalSeconds);
                    // Leave _retryNeeded = true, try again next cycle
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[KbSync] Retry failed unexpectedly — will retry on next cycle");
                    // Leave _retryNeeded = true
                }
            }

            // ── Poll pending ingestion jobs ──────────────────────────────────
            var jobsToCheck = new List<(string jobId, DateTime startedAt)>();
            while (_pendingJobs.TryDequeue(out var job))
                jobsToCheck.Add(job);

            foreach (var (jobId, startedAt) in jobsToCheck)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var kbDocSvc = scope.ServiceProvider.GetRequiredService<KbDocumentService>();
                    var status = await kbDocSvc.PollIngestionJobAsync(jobId);

                    if (status == "COMPLETE")
                    {
                        await MarkPendingDocumentsAsIngestedAsync(startedAt);
                        _logger.LogInformation("[KbSync] Job {JobId} COMPLETE — documents marked ingested", jobId);
                    }
                    else if (status == "FAILED")
                    {
                        await MarkPendingDocumentsAsFailedAsync(startedAt);
                        _logger.LogWarning("[KbSync] Job {JobId} FAILED — documents marked failed", jobId);
                    }
                    else if (status == "UNKNOWN")
                    {
                        // Poll error — re-enqueue unless job is too old
                        if (DateTime.UtcNow - startedAt < TimeSpan.FromHours(2))
                        {
                            _logger.LogWarning("[KbSync] Job {JobId} status UNKNOWN (poll error) — re-enqueueing", jobId);
                            _pendingJobs.Enqueue((jobId, startedAt));
                        }
                        else
                        {
                            _logger.LogWarning("[KbSync] Job {JobId} expired (>2h) with UNKNOWN status — dropping", jobId);
                        }
                    }
                    else if (status == "NOT_FOUND")
                    {
                        _logger.LogWarning("[KbSync] Job {JobId} not found in Bedrock — removing from queue as permanent failure", jobId);
                        // Do NOT re-enqueue. This is a permanent failure — the job ID no longer exists.
                        // This prevents an infinite retry loop for orphaned or stale job IDs.
                    }
                    else
                    {
                        // Still in progress: STARTING, IN_PROGRESS — re-enqueue unless job is too old
                        if (DateTime.UtcNow - startedAt < TimeSpan.FromHours(2))
                        {
                            _logger.LogDebug("[KbSync] Job {JobId} status={Status} — still in progress, re-enqueueing", jobId, status);
                            _pendingJobs.Enqueue((jobId, startedAt));
                        }
                        else
                        {
                            _logger.LogWarning("[KbSync] Job {JobId} expired (>2h) with status={Status} — dropping", jobId, status);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (DateTime.UtcNow - startedAt < TimeSpan.FromHours(2))
                    {
                        _logger.LogWarning(ex, "[KbSync] Failed to poll job {JobId} — re-enqueueing", jobId);
                        _pendingJobs.Enqueue((jobId, startedAt));
                    }
                    else
                    {
                        _logger.LogWarning(ex, "[KbSync] Failed to poll job {JobId} and expired (>2h) — dropping", jobId);
                    }
                }
            }
        }
    }

    /// <summary>Mark project_documents with pending status (uploaded before syncStartedAt) as ingested.</summary>
    private async Task MarkPendingDocumentsAsIngestedAsync(DateTime syncStartedAt)
    {
        using var scope = _scopeFactory.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE project_documents SET IngestionStatus = 'ingested', IngestedAt = {0} WHERE IngestionStatus = 'pending' AND UploadedAt <= {1}",
            DateTime.UtcNow, syncStartedAt);
        _logger.LogInformation("[KbSync] Marked pending project documents as ingested (sync started {SyncStartedAt:u})", syncStartedAt);
    }

    /// <summary>Mark project_documents with pending status (uploaded before syncStartedAt) as failed.</summary>
    private async Task MarkPendingDocumentsAsFailedAsync(DateTime syncStartedAt)
    {
        using var scope = _scopeFactory.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE project_documents SET IngestionStatus = 'failed' WHERE IngestionStatus = 'pending' AND UploadedAt <= {0}",
            syncStartedAt);
        _logger.LogWarning("[KbSync] Marked pending project documents as failed (sync started {SyncStartedAt:u})", syncStartedAt);
    }
}
