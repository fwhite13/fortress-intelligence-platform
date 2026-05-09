using FortressAI.V2.Web.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

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

    public void RequestRetry()
    {
        _retryNeeded = true;
        _logger.LogInformation("[KbSync] Retry requested — will attempt on next 30s cycle");
    }

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

            if (_retryNeeded)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var kbDocSvc = scope.ServiceProvider.GetRequiredService<KbDocumentService>();
                    await kbDocSvc.StartIngestionAsync(throwOnConflict: true);
                    _retryNeeded = false;
                    _logger.LogInformation("[KbSync] Retry succeeded — flag cleared");
                }
                catch (Amazon.BedrockAgent.Model.ConflictException)
                {
                    _logger.LogInformation("[KbSync] Still busy — will retry in {Seconds}s", RetryInterval.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[KbSync] Retry failed unexpectedly — will retry on next cycle");
                }
            }

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
                        _logger.LogInformation("[KbSync] Job {JobId} COMPLETE", jobId);
                    }
                    else if (status == "FAILED")
                    {
                        _logger.LogWarning("[KbSync] Job {JobId} FAILED", jobId);
                    }
                    else if (status == "UNKNOWN")
                    {
                        if (DateTime.UtcNow - startedAt < TimeSpan.FromHours(2))
                        {
                            _logger.LogWarning("[KbSync] Job {JobId} status UNKNOWN — re-enqueueing", jobId);
                            _pendingJobs.Enqueue((jobId, startedAt));
                        }
                        else
                        {
                            _logger.LogWarning("[KbSync] Job {JobId} expired (>2h) with UNKNOWN status — dropping", jobId);
                        }
                    }
                    else
                    {
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
}
