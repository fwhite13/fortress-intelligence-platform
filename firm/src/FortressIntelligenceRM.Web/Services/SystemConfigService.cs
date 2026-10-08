using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

public class SystemConfigService : ISystemConfigService
{
    public const string ZoomBotUserIdKey = "zoom_bot_user_id";

    private readonly IDbContextFactory<FirmDbContext> _dbFactory;

    public SystemConfigService(IDbContextFactory<FirmDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<string?> GetAsync(string key)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.SystemConfig
            .Where(c => c.ConfigKey == key)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync();
    }

    public async Task SetAsync(string key, string value)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var record = await db.SystemConfig.FirstOrDefaultAsync(c => c.ConfigKey == key);
        if (record == null)
        {
            db.SystemConfig.Add(new FirmSystemConfig { ConfigKey = key, ConfigValue = value, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            record.ConfigValue = value;
            record.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(string key)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var record = await db.SystemConfig.FirstOrDefaultAsync(c => c.ConfigKey == key);
        if (record == null)
            return;
        db.SystemConfig.Remove(record);
        await db.SaveChangesAsync();
    }

    public async Task<Guid?> GetZoomBotUserIdAsync()
    {
        var value = await GetAsync(ZoomBotUserIdKey);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public Task SetZoomBotUserIdAsync(Guid userId) => SetAsync(ZoomBotUserIdKey, userId.ToString());
}
