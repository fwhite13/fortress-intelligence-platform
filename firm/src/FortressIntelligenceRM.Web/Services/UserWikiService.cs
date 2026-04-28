using FortressIntelligenceRM.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FortressIntelligenceRM.Web.Services;

public class UserWikiService : IUserWikiService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly ILogger<UserWikiService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);
    private const int MaxEntries = 100;

    public UserWikiService(IDbContextFactory<FirmDbContext> dbFactory, ILogger<UserWikiService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<OrgContextEntry>> GetEntriesAsync(string entraOid, string tenantId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var wiki = await db.UserWikis.FirstOrDefaultAsync(w => w.EntraOid == entraOid && w.EntraTenantId == tenantId);
            if (wiki == null || string.IsNullOrWhiteSpace(wiki.WikiContent))
                return [];

            var content = wiki.WikiContent.Trim();
            if (content.StartsWith('['))
            {
                try
                {
                    var entries = JsonSerializer.Deserialize<List<OrgContextEntry>>(content, _jsonOpts);
                    return entries ?? [];
                }
                catch (JsonException)
                {
                    return [];
                }
            }

            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserWiki] GetEntriesAsync failed for user {EntraOid}", entraOid);
            return [];
        }
    }

    public async Task UpsertEntriesAsync(string entraOid, string tenantId, List<OrgContextEntry> entries, string updatedBy)
    {
        if (entries.Count > MaxEntries)
            entries = entries.Take(MaxEntries).ToList();

        var json = JsonSerializer.Serialize(entries, _jsonOpts);
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO firm_user_wiki (entra_oid, entra_tenant_id, wiki_content, updated_at, updated_by)
              VALUES ({0}, {1}, {2}, UTC_TIMESTAMP(), {3})
              ON DUPLICATE KEY UPDATE wiki_content = VALUES(wiki_content),
                  updated_at = UTC_TIMESTAMP(),
                  updated_by = VALUES(updated_by)",
            entraOid, tenantId, json, updatedBy);
    }

    public async Task<DateTime?> GetUpdatedAtAsync(string entraOid, string tenantId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var wiki = await db.UserWikis.FirstOrDefaultAsync(w => w.EntraOid == entraOid && w.EntraTenantId == tenantId);
            return wiki?.UpdatedAt;
        }
        catch { return null; }
    }
}
