using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using FortressAI.Web.Data;
using FortressAI.Web.Hubs;
using FortressAI.Shared.Models;

namespace FortressAI.Web.Services;

public class EmailAlertService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<EmailAlertService> _logger;

    public EmailAlertService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHubContext<DashboardHub> hubContext,
        ILogger<EmailAlertService> logger)
    {
        _dbFactory = dbFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Stores a new email alert and pushes it to the user's dashboard via SignalR.
    /// </summary>
    public async Task<EmailAlert> CreateAlertAsync(Guid userId, string messageId, string senderEmail,
        string subject, string importance, string? summary, string? suggestedResponse)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var alert = new EmailAlert
        {
            UserId = userId,
            MessageId = messageId,
            SenderEmail = senderEmail,
            Subject = subject,
            Importance = importance,
            Summary = summary,
            SuggestedResponse = suggestedResponse
        };

        db.EmailAlerts.Add(alert);
        await db.SaveChangesAsync();

        // Push to dashboard via SignalR
        await _hubContext.Clients.Group($"user-{userId}").SendAsync("ReceiveEmailAlert", new
        {
            alert.Id,
            alert.MessageId,
            alert.SenderEmail,
            alert.Subject,
            alert.Importance,
            alert.Summary,
            alert.SuggestedResponse,
            alert.CreatedAt
        });

        _logger.LogInformation("Created email alert {AlertId} for user {UserId}: {Subject}", alert.Id, userId, subject);
        return alert;
    }

    /// <summary>
    /// Gets undismissed alerts for a user, ordered by most recent.
    /// </summary>
    public async Task<List<EmailAlert>> GetActiveAlertsAsync(Guid userId, int limit = 20)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.EmailAlerts
            .Where(a => a.UserId == userId && !a.Dismissed)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Dismisses an alert.
    /// </summary>
    public async Task DismissAlertAsync(int alertId, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var alert = await db.EmailAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.UserId == userId);
        if (alert != null)
        {
            alert.Dismissed = true;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Logs an email (for medium/low importance emails that don't generate alerts).
    /// </summary>
    public async Task LogEmailAsync(Guid userId, string messageId, string senderEmail,
        string subject, string importance, DateTime receivedAt)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.EmailLogs.Add(new EmailLog
        {
            UserId = userId,
            MessageId = messageId,
            SenderEmail = senderEmail,
            Subject = subject,
            Importance = importance,
            ReceivedAt = receivedAt
        });
        await db.SaveChangesAsync();
    }
}
