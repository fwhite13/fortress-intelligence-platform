using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

public class BriefingService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<BriefingService> _logger;

    public BriefingService(IDbContextFactory<AppDbContext> dbFactory, ILogger<BriefingService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<BriefingHistory?> GetTodaysBriefingAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await db.BriefingHistories
            .Where(b => b.UserId == userId && b.BriefingDate == today)
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<BriefingHistory>> GetRecentBriefingsAsync(Guid userId, int count = 7)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.BriefingHistories
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.BriefingDate)
            .Take(count)
            .ToListAsync();
    }

    public async Task<BriefingHistory> StoreBriefingAsync(Guid userId, DateOnly date, string content, string? emailSummary = null, string? calendarEventsJson = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var briefing = new BriefingHistory
        {
            UserId = userId,
            BriefingDate = date,
            Content = content,
            EmailSummary = emailSummary,
            CalendarEventsJson = calendarEventsJson
        };
        db.BriefingHistories.Add(briefing);
        await db.SaveChangesAsync();
        _logger.LogInformation("Stored briefing for user {UserId} date {Date}", userId, date);
        return briefing;
    }
}
