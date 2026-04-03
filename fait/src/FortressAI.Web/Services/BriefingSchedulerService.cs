using Microsoft.EntityFrameworkCore;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

public class BriefingSchedulerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BriefingSchedulerService> _logger;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    public BriefingSchedulerService(IServiceProvider services, ILogger<BriefingSchedulerService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Allow app to warm up before first check
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAndDeliverBriefingsAsync(stoppingToken);
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckAndDeliverBriefingsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var briefingSvc = scope.ServiceProvider.GetRequiredService<BriefingService>();
            var generationSvc = scope.ServiceProvider.GetRequiredService<BriefingGenerationService>();

            var schedules = await db.UserBriefingSchedules.ToListAsync(ct);

            if (!schedules.Any())
            {
                _logger.LogDebug("[BRIEFING_SCHEDULER] No user schedules found — skipping cycle");
                return;
            }

            var now = DateTime.UtcNow;
            var nowMinutes = now.Hour * 60 + now.Minute;

            foreach (var schedule in schedules)
            {
                if (ct.IsCancellationRequested) break;

                var scheduledMinutes = schedule.DeliveryTimeUtc.Hour * 60 + schedule.DeliveryTimeUtc.Minute;
                var delta = Math.Abs(nowMinutes - scheduledMinutes);

                if (delta > 1)
                {
                    _logger.LogDebug("[BRIEFING_SCHEDULER] User {UserId} — outside delivery window (delta={Delta}min), skipping",
                        schedule.UserId, delta);
                    continue;
                }

                // Within delivery window — check if briefing already exists today
                var existing = await briefingSvc.GetTodaysBriefingAsync(schedule.UserId);
                if (existing != null)
                {
                    _logger.LogInformation("[BRIEFING_SCHEDULER] User {UserId} — briefing already delivered today, skipping",
                        schedule.UserId);
                    continue;
                }

                // Trigger generation
                _logger.LogInformation("[BRIEFING_SCHEDULER] User {UserId} — delivery window matched, triggering briefing generation",
                    schedule.UserId);

                try
                {
                    var result = await generationSvc.GenerateBriefingAsync(schedule.UserId);
                    if (result.Success)
                    {
                        _logger.LogInformation("[BRIEFING_SCHEDULER] User {UserId} — briefing delivered successfully (BriefingId={BriefingId})",
                            schedule.UserId, result.Briefing?.Id);
                    }
                    else
                    {
                        _logger.LogWarning("[BRIEFING_SCHEDULER] User {UserId} — briefing generation failed: {Error}",
                            schedule.UserId, result.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[BRIEFING_SCHEDULER] User {UserId} — unexpected error during briefing generation (non-fatal)",
                        schedule.UserId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — do not log as error
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BRIEFING_SCHEDULER] Scheduler cycle failed (non-fatal)");
        }
    }
}
