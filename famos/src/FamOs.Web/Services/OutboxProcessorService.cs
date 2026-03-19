using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;

namespace FamOs.Web.Services;

public class OutboxProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _services;
    private readonly ILogger<OutboxProcessorService> _logger;

    public OutboxProcessorService(IServiceScopeFactory services,
        ILogger<OutboxProcessorService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Outbox] Batch processing error");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task ProcessBatchAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();

        var events = await db.OutboxEvents
            .Where(e => !e.Processed && e.RetryCount < 5)
            .OrderBy(e => e.OccurredAt)
            .Take(50)
            .ToListAsync();

        if (!events.Any()) return;

        foreach (var evt in events)
        {
            try
            {
                _logger.LogInformation("[Outbox] Processing {EventType}: {Payload}",
                    evt.EventType, evt.PayloadJson);

                evt.Processed   = true;
                evt.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                evt.RetryCount++;
                evt.ErrorMessage = ex.Message;
                _logger.LogWarning(ex, "[Outbox] Failed event {Id}, retry {N}",
                    evt.Id, evt.RetryCount);
            }
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("[Outbox] Processed {Count} events", events.Count);
    }
}
