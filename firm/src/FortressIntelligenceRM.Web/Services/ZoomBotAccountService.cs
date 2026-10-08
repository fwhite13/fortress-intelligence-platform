using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

// WI #8043 — shared Zoom bot account stored in firm_zoom_bot_account (single row, id = 1).
// Zoom HTTP calls (refresh, OBF) are delegated to ZoomOAuthService, which owns the app credentials.
public class ZoomBotAccountService : IZoomBotAccountService
{
    private const int RowId = 1;

    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IZoomOAuthService _zoomOAuthService;
    private readonly ILogger<ZoomBotAccountService> _logger;

    public ZoomBotAccountService(
        IDbContextFactory<FirmDbContext> dbFactory,
        IZoomOAuthService zoomOAuthService,
        ILogger<ZoomBotAccountService> logger)
    {
        _dbFactory = dbFactory;
        _zoomOAuthService = zoomOAuthService;
        _logger = logger;
    }

    public async Task<FirmZoomBotAccount?> GetAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ZoomBotAccount.AsNoTracking().FirstOrDefaultAsync(b => b.Id == RowId);
    }

    public async Task SaveAsync(string? email, string zoomUserId, string accessToken, string refreshToken, DateTime expiresAt)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var record = await db.ZoomBotAccount.FirstOrDefaultAsync(b => b.Id == RowId);
        if (record == null)
        {
            record = new FirmZoomBotAccount { Id = RowId };
            db.ZoomBotAccount.Add(record);
        }

        record.Email = email;
        record.ZoomUserId = zoomUserId;
        record.AccessToken = accessToken;
        record.RefreshToken = refreshToken;
        record.ExpiresAt = expiresAt;
        record.ConnectedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task ClearAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var record = await db.ZoomBotAccount.FirstOrDefaultAsync(b => b.Id == RowId);
        if (record == null)
            return;
        db.ZoomBotAccount.Remove(record);
        await db.SaveChangesAsync();
    }

    public async Task<string?> GetObfTokenAsync(long meetingId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var record = await db.ZoomBotAccount.FirstOrDefaultAsync(b => b.Id == RowId);
            if (record == null || string.IsNullOrEmpty(record.AccessToken))
                return null;

            if ((record.ExpiresAt ?? DateTime.MinValue) < DateTime.UtcNow.AddMinutes(5) && !await RefreshAccessTokenAsync(db, record))
                return null;

            var (ok, authError, token) = await _zoomOAuthService.FetchObfTokenAsync(record.AccessToken!, meetingId);
            if (ok)
                return token;
            if (!authError)
                return null;

            if (!await RefreshAccessTokenAsync(db, record))
                return null;

            var (ok2, _, token2) = await _zoomOAuthService.FetchObfTokenAsync(record.AccessToken!, meetingId);
            return ok2 ? token2 : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FIRM: Failed to obtain Zoom OBF token from bot account for meeting {MeetingId}", meetingId);
            return null;
        }
    }

    public async Task<string?> GetEmailAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ZoomBotAccount
            .Where(b => b.Id == RowId)
            .Select(b => b.Email)
            .FirstOrDefaultAsync();
    }

    private async Task<bool> RefreshAccessTokenAsync(FirmDbContext db, FirmZoomBotAccount record)
    {
        if (string.IsNullOrEmpty(record.RefreshToken))
            return false;

        var refreshed = await _zoomOAuthService.RefreshTokensAsync(record.RefreshToken);
        if (refreshed == null)
        {
            _logger.LogWarning("FIRM: Zoom bot account token refresh failed ({Email})", record.Email);
            return false;
        }

        record.AccessToken = refreshed.AccessToken;
        if (refreshed.RefreshToken != null)
            record.RefreshToken = refreshed.RefreshToken;
        record.ExpiresAt = refreshed.ExpiresAt;
        await db.SaveChangesAsync();
        return true;
    }
}
