using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public class OpportunitySearchService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;

    public OpportunitySearchService(IDbContextFactory<FamOsDbContext> dbFactory)
        => _dbFactory = dbFactory;

    /// <summary>Returns up to 8 opportunities whose name contains the query. Excludes closed.</summary>
    public async Task<List<OpportunitySearchResult>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new();

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Opportunities
            .Where(o => !o.IsClosed && EF.Functions.Like(o.Name, $"%{query}%"))
            .OrderBy(o => o.Name)
            .Take(8)
            .Select(o => new OpportunitySearchResult
            {
                Id      = o.Id,
                Name    = o.Name,
                Stage   = o.LifecycleStage,
                Signal  = o.DominantSignal,
                Premium = o.EstimatedPremium,
            })
            .ToListAsync();
    }
}

public class OpportunitySearchResult
{
    public Guid           Id      { get; set; }
    public string         Name    { get; set; } = "";
    public LifecycleStage Stage   { get; set; }
    public DominantSignal Signal  { get; set; }
    public decimal?       Premium { get; set; }
}
