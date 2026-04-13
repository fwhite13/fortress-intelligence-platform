using FortressIntelligenceRM.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

public class OrgContextService : IOrgContextService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly ILogger<OrgContextService> _logger;

    public OrgContextService(IDbContextFactory<FirmDbContext> dbFactory, ILogger<OrgContextService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<OrgContextDto?> GetContextAsync(string tenantId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var ctx = await db.OrgContexts.FirstOrDefaultAsync(o => o.EntraTenantId == tenantId);
            if (ctx == null) return null;
            return new OrgContextDto
            {
                WikiContent = ctx.WikiContent,
                UpdatedAt = ctx.UpdatedAt,
                UpdatedBy = ctx.UpdatedBy
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrgContext] GetContextAsync failed for tenant {TenantId}", tenantId);
            return null;
        }
    }

    public async Task UpsertContextAsync(string tenantId, string content, string updatedBy)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO firm_org_context (entra_tenant_id, wiki_content, updated_at, updated_by)
              VALUES ({0}, {1}, UTC_TIMESTAMP(), {2})
              ON DUPLICATE KEY UPDATE wiki_content = VALUES(wiki_content),
                  updated_at = UTC_TIMESTAMP(),
                  updated_by = VALUES(updated_by)",
            tenantId, content, updatedBy);
    }
}
