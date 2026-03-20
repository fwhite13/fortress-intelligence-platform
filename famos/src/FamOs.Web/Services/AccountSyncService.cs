using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public interface IAccountSyncService
{
    /// <summary>Sync HubSpot companies for a given affinity group into the accounts table.</summary>
    Task SyncAsync(string affinityId, CancellationToken ct = default);
    Task RefreshOppCountsAsync(string affinityId);
}

public class AccountSyncService : BackgroundService, IAccountSyncService
{
    private readonly IServiceScopeFactory        _services;
    private readonly ILogger<AccountSyncService> _logger;

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    public AccountSyncService(IServiceScopeFactory services,
        ILogger<AccountSyncService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    // ── BackgroundService ─────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), ct); // let startup settle

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var config = scope.ServiceProvider
                    .GetRequiredService<IOptions<AffinityConfig>>()
                    .Value;

                foreach (var group in config.AffinityGroups.Any()
                    ? config.AffinityGroups.Select(g => g.AffinityId)
                    : new[] { config.AffinityId })
                {
                    await SyncCoreAsync(scope, group, ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[AccountSync] Background sync error");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }

    // ── IAccountSyncService ───────────────────────────────────────────────

    public async Task SyncAsync(string affinityId, CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        await SyncCoreAsync(scope, affinityId, ct);
    }

    public async Task RefreshOppCountsAsync(string affinityId)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();

        var accounts = await db.Accounts
            .Where(a => a.AffinityId == affinityId)
            .ToListAsync();

        foreach (var account in accounts)
        {
            account.ActiveOppCount = await db.Opportunities
                .CountAsync(o => !o.IsClosed
                    && o.AffinityId == affinityId
                    && EF.Functions.Like(o.Name, $"%{account.CompanyName}%"));
        }

        await db.SaveChangesAsync();
    }

    // ── Core sync logic ───────────────────────────────────────────────────

    private async Task SyncCoreAsync(IServiceScope scope, string affinityId,
        CancellationToken ct)
    {
        var config = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var serviceKey = config["HubSpot:ServiceKey"];

        if (string.IsNullOrEmpty(serviceKey))
        {
            _logger.LogDebug("[AccountSync] HubSpot:ServiceKey not set — skipping sync for {Aff}", affinityId);
            return;
        }

        var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var client  = factory.CreateClient("HubSpot");
        var db      = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();

        // Fetch companies from HubSpot (paginated, max 100 per request)
        var companies = new List<HsCompany>();
        string? after = null;

        do
        {
            var url = "/crm/v3/objects/companies?limit=100&properties=name,city,state" +
                (after != null ? $"&after={after}" : "");
            var resp = await client.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[AccountSync] HubSpot companies fetch failed: {Status}", resp.StatusCode);
                break;
            }

            var page = await resp.Content.ReadFromJsonAsync<HsCompanyPage>(Opts, ct);
            if (page?.Results != null)
                companies.AddRange(page.Results);

            after = page?.Paging?.Next?.After;
        } while (after != null && companies.Count < 1000); // safety cap

        _logger.LogInformation("[AccountSync] Fetched {Count} companies for {Aff}", companies.Count, affinityId);

        // Upsert into accounts table
        var now = DateTime.UtcNow;
        foreach (var company in companies)
        {
            if (string.IsNullOrEmpty(company.Properties?.Name)) continue;

            var existing = await db.Accounts
                .FirstOrDefaultAsync(a => a.HubSpotId == company.Id && a.AffinityId == affinityId, ct);

            if (existing == null)
            {
                db.Accounts.Add(new Account
                {
                    AffinityId   = affinityId,
                    CompanyName  = company.Properties.Name,
                    HubSpotId    = company.Id,
                    City         = company.Properties.City,
                    State        = company.Properties.State,
                    LastSyncedAt = now,
                });
            }
            else
            {
                existing.CompanyName  = company.Properties.Name;
                existing.City         = company.Properties.City;
                existing.State        = company.Properties.State;
                existing.LastSyncedAt = now;
                existing.UpdatedAt    = now;
            }
        }

        await db.SaveChangesAsync(ct);
        await RefreshOppCountsAsync(affinityId);
        _logger.LogInformation("[AccountSync] Sync complete for {Aff}", affinityId);
    }

    // ── HubSpot DTOs ──────────────────────────────────────────────────────

    private class HsCompanyPage
    {
        public List<HsCompany>? Results { get; set; }
        public HsPaging?        Paging  { get; set; }
    }

    private class HsCompany
    {
        public string?             Id         { get; set; }
        public HsCompanyProps?     Properties { get; set; }
    }

    private class HsCompanyProps
    {
        public string? Name  { get; set; }
        public string? City  { get; set; }
        public string? State { get; set; }
    }

    private class HsPaging
    {
        public HsPagingNext? Next { get; set; }
    }

    private class HsPagingNext
    {
        public string? After { get; set; }
    }
}
