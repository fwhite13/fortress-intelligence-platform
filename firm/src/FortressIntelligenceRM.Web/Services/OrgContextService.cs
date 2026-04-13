using FortressIntelligenceRM.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FortressIntelligenceRM.Web.Services;

public class OrgContextService : IOrgContextService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly ILogger<OrgContextService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public OrgContextService(IDbContextFactory<FirmDbContext> dbFactory, ILogger<OrgContextService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<OrgContextEntry>> GetContextAsync(string tenantId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var ctx = await db.OrgContexts.FirstOrDefaultAsync(o => o.EntraTenantId == tenantId);
            if (ctx == null || string.IsNullOrWhiteSpace(ctx.WikiContent))
                return [];

            var content = ctx.WikiContent.Trim();

            // Try JSON array first
            if (content.StartsWith('['))
            {
                try
                {
                    var entries = JsonSerializer.Deserialize<List<OrgContextEntry>>(content, _jsonOpts);
                    return entries ?? [];
                }
                catch (JsonException)
                {
                    // Fall through to legacy handling
                }
            }

            // Legacy plain text: wrap as single entry
            return [new OrgContextEntry("Legacy Content", content)];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrgContext] GetContextAsync failed for tenant {TenantId}", tenantId);
            return [];
        }
    }

    public async Task<DateTime?> GetUpdatedAtAsync(string tenantId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var ctx = await db.OrgContexts.FirstOrDefaultAsync(o => o.EntraTenantId == tenantId);
            return ctx?.UpdatedAt;
        }
        catch { return null; }
    }

    public async Task<string?> GetUpdatedByAsync(string tenantId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var ctx = await db.OrgContexts.FirstOrDefaultAsync(o => o.EntraTenantId == tenantId);
            return ctx?.UpdatedBy;
        }
        catch { return null; }
    }

    public async Task UpsertContextAsync(string tenantId, List<OrgContextEntry> entries, string updatedBy)
    {
        var json = JsonSerializer.Serialize(entries, _jsonOpts);
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO firm_org_context (entra_tenant_id, wiki_content, updated_at, updated_by)
              VALUES ({0}, {1}, UTC_TIMESTAMP(), {2})
              ON DUPLICATE KEY UPDATE wiki_content = VALUES(wiki_content),
                  updated_at = UTC_TIMESTAMP(),
                  updated_by = VALUES(updated_by)",
            tenantId, json, updatedBy);
    }
}
