using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;

namespace FamOs.Web.Services;

public class IntakeSessionService : IIntakeSessionService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;

    public IntakeSessionService(IDbContextFactory<FamOsDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<(long SessionId, string OtpCode)> CreateOrRefreshSessionAsync(string opportunityId, string email)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var session = await db.IntakeSessions
            .FirstOrDefaultAsync(s => s.OpportunityId == opportunityId && s.Email == email);

        var code = Random.Shared.Next(100000, 999999).ToString();

        if (session is null)
        {
            session = new Data.Entities.IntakeSession
            {
                OpportunityId = opportunityId,
                Email = email,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };
            db.IntakeSessions.Add(session);
        }

        session.OtpCode = code;
        session.OtpExpiresAt = DateTime.UtcNow.AddMinutes(10);
        session.IsVerified = false;
        session.ExpiresAt = DateTime.UtcNow.AddDays(30);

        await db.SaveChangesAsync();
        return (session.Id, code);
    }

    public async Task<string?> VerifyOtpAsync(long sessionId, string otpCode)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var session = await db.IntakeSessions.FindAsync(sessionId)
            ?? throw new InvalidOperationException("Invalid or expired verification code");

        if (session.OtpCode != otpCode || session.OtpExpiresAt is null || session.OtpExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("Invalid or expired verification code");

        session.IsVerified = true;
        await db.SaveChangesAsync();
        return session.LastPage;
    }

    public async Task UpdateLastPageAsync(long sessionId, string pageName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.IntakeSessions.FindAsync(sessionId);
        if (session is null) return;
        session.LastPage = pageName;
        await db.SaveChangesAsync();
    }

    public async Task CompleteSessionAsync(long sessionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.IntakeSessions.FindAsync(sessionId);
        if (session is null) return;
        session.ExpiresAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
