using Microsoft.Extensions.Options;

namespace FortressIntelligenceRM.Web.Services.Plaud;

/// <summary>
/// WI #9135 — Plaud auto-sync poller (spec §5.2), following <see cref="StuckMeetingRecoveryService"/>:
/// a BackgroundService with a PeriodicTimer.
///
///   - Registered always, but returns at once unless Firm:Plaud:Enabled (dev only until promotion, D9), and
///     also when Firm:Plaud:RedirectUri is unset (no oauth_env to match, so no account is ours).
///   - 2-minute startup delay, then a 60 s tick.
///   - Each tick selects at most 5 due users (<see cref="PlaudSyncService.GetDueUserIdsAsync"/>: auto_sync = 1,
///     state not in (reconnect_needed, disconnected), next_sync_at &lt;= now, account oauth_env = this instance's
///     redirect host) and runs them one at a time. Each user is claimed with the single conditional UPDATE
///     (<see cref="PlaudSyncService.TryClaimUserAsync"/>; zero rows affected = another task owns it, skip) and
///     the claim is released in finally with CancellationToken.None.
///   - While the instance is paused after 3 × 429 in 5 min (<see cref="PlaudRateLimiter"/>), ticks do nothing.
///   - Schedule, jitter, backoff and Retry-After are written by the run itself (PlaudSyncService.ComputeNextSyncAt).
/// Logs carry user ids and counts only.
/// </summary>
public class PlaudSyncBackgroundService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlaudRateLimiter _rateLimiter;
    private readonly PlaudOptions _options;
    private readonly ILogger<PlaudSyncBackgroundService> _logger;

    public PlaudSyncBackgroundService(IServiceScopeFactory scopeFactory, PlaudRateLimiter rateLimiter,
        IOptions<PlaudOptions> options, ILogger<PlaudSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _rateLimiter = rateLimiter;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("[PlaudSync] auto-sync poller not started: Firm:Plaud:Enabled is false");
            return;
        }
        if (!Uri.TryCreate(_options.RedirectUri, UriKind.Absolute, out _))
        {
            _logger.LogWarning("[PlaudSync] auto-sync poller not started: Firm:Plaud:RedirectUri is not set");
            return;
        }

        _logger.LogInformation("[PlaudSync] auto-sync poller starting in {Delay}; tick {Tick}, {Users} users per tick",
            StartupDelay, Tick, PlaudSyncService.DueUsersPerTick);
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Tick);
        try
        {
            do
            {
                try
                {
                    await RunTickAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("[PlaudSync] tick failed: {Error}", ex.GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task RunTickAsync(CancellationToken stoppingToken)
    {
        if (_rateLimiter.PausedFor is { } paused)
        {
            _logger.LogDebug("[PlaudSync] tick skipped: instance paused for {Seconds}s", (int)paused.TotalSeconds);
            return;
        }

        List<Guid> due;
        using (var scope = _scopeFactory.CreateScope())
        {
            var sync = scope.ServiceProvider.GetRequiredService<PlaudSyncService>();
            due = await sync.GetDueUserIdsAsync(PlaudSyncService.DueUsersPerTick, stoppingToken);
        }
        if (due.Count == 0)
            return;

        foreach (var userId in due)
        {
            stoppingToken.ThrowIfCancellationRequested();
            if (_rateLimiter.PausedFor != null)
                break;
            // A scope per user: fresh scoped services (MCP session, DbContexts) for each run.
            using var scope = _scopeFactory.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<PlaudSyncService>();
            await RunUserAsync(sync, userId, stoppingToken);
        }
    }

    private async Task RunUserAsync(PlaudSyncService sync, Guid userId, CancellationToken stoppingToken)
    {
        var claimToken = PlaudSyncService.NewClaimToken();
        // Single conditional UPDATE; the affected-row count decides ownership. 0 = another task owns it
        // (or it is no longer due), so skip.
        if (!await sync.TryClaimUserAsync(userId, claimToken, requireDue: true, stoppingToken))
            return;
        try
        {
            await sync.SyncUserAsync(userId, Models.PlaudSyncTrigger.Auto, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[PlaudSync] user={UserId} auto run failed: {Error}", userId, ex.GetType().Name);
            await sync.RecordRunErrorAsync(userId, "sync_error");
        }
        finally
        {
            await sync.ReleaseUserClaimAsync(userId, claimToken, CancellationToken.None);
        }
    }
}
