using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class V1ProjectQueryService : IV1ProjectQueryService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly ILogger<V1ProjectQueryService> _logger;

    public V1ProjectQueryService(
        IDbContextFactory<FaitV2DbContext> dbFactory,
        ILogger<V1ProjectQueryService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<FaitV1Project>> GetV1ProjectsForUserAsync(string entraOid, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Cross-schema query: fait_dev.projects on same Aurora cluster
            // Join to fait_dev.app_users by entra_oid to filter to this user's projects
            var results = await db.Database
                .SqlQueryRaw<FaitV1Project>(
                    @"SELECT p.id, p.name, p.description, p.custom_instructions, p.created_at, p.updated_at
                      FROM fait_dev.projects p
                      INNER JOIN fait_dev.app_users u ON u.id = p.user_id
                      WHERE u.entra_oid = {0}
                      ORDER BY p.updated_at DESC
                      LIMIT 50",
                    entraOid)
                .ToListAsync(ct);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query FAIT v1 projects for entraOid={EntraOid}; returning empty list", entraOid);
            return new List<FaitV1Project>();
        }
    }
}
