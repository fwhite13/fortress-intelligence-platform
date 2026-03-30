using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

public class TranscriptPollingService : IHostedService, IDisposable
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly TeamsGraphService _graphService;
    private readonly ILogger<TranscriptPollingService> _logger;
    private readonly IConfiguration _config;
    private Timer? _timer;

    public TranscriptPollingService(
        IDbContextFactory<FirmDbContext> dbFactory,
        TeamsGraphService graphService,
        ILogger<TranscriptPollingService> logger,
        IConfiguration config)
    {
        _dbFactory = dbFactory;
        _graphService = graphService;
        _logger = logger;
        _config = config;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var intervalMinutes = _config.GetValue<int>("Firm:TranscriptPollIntervalMinutes", 2);
        _logger.LogInformation("[TranscriptPolling] Service started. Poll interval: {Minutes}m", intervalMinutes);
        _timer = new Timer(PollAsync, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(intervalMinutes));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async void PollAsync(object? state)
    {
        try
        {
            await PollCoreAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TranscriptPolling] Unhandled error in poll cycle");
        }
    }

    private async Task PollCoreAsync(CancellationToken ct)
    {
        var maxPollingHours = _config.GetValue<int>("Firm:TranscriptPollMaxHours", 2);
        var cutoff = DateTime.UtcNow.AddHours(-maxPollingHours);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var fifteenMinutesAgo = DateTime.UtcNow.AddMinutes(-15);
            var meetingRows = await db.Meetings
                .Where(m => m.Status == MeetingStatus.WaitingTranscript
                    && m.Mode == "A"
                    && m.StartDatetime != null
                    && m.StartDatetime < fifteenMinutesAgo
                    && m.StartDatetime > cutoff)
                .Join(db.Users,
                    m => m.CreatedBy,
                    u => u.Id,
                    (m, u) => new TranscriptPollRow
                    {
                        MeetingId = m.Id,
                        MeetingUrl = m.MeetingUrl,
                        StartDatetime = m.StartDatetime,
                        EntraOid = u.EntraOid
                    })
                .ToListAsync(ct);

            if (meetingRows.Count == 0) return;

            _logger.LogInformation("[TranscriptPolling] Found {Count} Mode A meetings to poll for transcripts.", meetingRows.Count);

            foreach (var row in meetingRows)
            {
                if (string.IsNullOrEmpty(row.MeetingUrl) || string.IsNullOrEmpty(row.EntraOid))
                    continue;

                _logger.LogInformation("[TranscriptPolling] Polling transcript for meeting {MeetingId}", row.MeetingId);
                var found = await _graphService.TryFetchTranscriptForMeetingAsync(
                    row.MeetingId, row.MeetingUrl, row.EntraOid, ct);

                if (!found)
                    _logger.LogInformation("[TranscriptPolling] Transcript not yet available for meeting {MeetingId} — will retry.", row.MeetingId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TranscriptPolling] Poll cycle failed (mode column may not exist yet) — will retry next interval");
        }
    }

    private class TranscriptPollRow
    {
        public long MeetingId { get; set; }
        public string? MeetingUrl { get; set; }
        public DateTime? StartDatetime { get; set; }
        public string EntraOid { get; set; } = "";
    }
}
