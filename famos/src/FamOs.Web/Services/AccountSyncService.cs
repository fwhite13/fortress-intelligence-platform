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

/// <summary>
/// ADO#1016: Expanded sync to include lifecyclestage → AccountStatus mapping
/// and primary deal data (coverage, carrier, expiration) from associated deals.
/// </summary>
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

    // HubSpot rate limit: 100 req/10s — add small delays
    private const int RateLimitDelayMs = 60;

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

        // ── Step 1: Fetch companies with expanded properties ──────────────
        var companies = await FetchAllCompaniesAsync(client, ct);
        _logger.LogInformation("[AccountSync] Fetched {Count} companies for {Aff}", companies.Count, affinityId);

        if (!companies.Any()) return;

        // ── Step 2: Fetch all deals in bulk ───────────────────────────────
        var deals = await FetchAllDealsAsync(client, ct);
        _logger.LogInformation("[AccountSync] Fetched {Count} deals", deals.Count);

        if (!deals.Any())
        {
            _logger.LogWarning("[AccountSync] No deals found in HubSpot — coverage/carrier/expiration will be empty for all accounts");
        }

        // ── Step 3: Fetch company→deal associations ───────────────────────
        var companyDeals = await FetchCompanyDealAssociationsAsync(client, companies, ct);
        _logger.LogInformation("[AccountSync] Fetched associations for {Count} companies", companyDeals.Count);

        // Build deal lookup by ID
        var dealLookup = deals.ToDictionary(d => d.Id ?? "", d => d);

        // ── Step 4: Upsert accounts with mapped data ──────────────────────
        var now = DateTime.UtcNow;

        // Pre-load all existing accounts for this affinityId to avoid N+1 queries
        var existingAccounts = await db.Accounts
            .Where(a => a.AffinityId == affinityId)
            .ToDictionaryAsync(a => a.HubSpotId ?? "", a => a, ct);

        foreach (var company in companies)
        {
            if (string.IsNullOrEmpty(company.Properties?.Name)) continue;

            try
            {
                // Find primary deal for this company
                HsDeal? primaryDeal = null;
                if (companyDeals.TryGetValue(company.Id ?? "", out var dealIds))
                {
                    primaryDeal = PickPrimaryDeal(dealIds, dealLookup);
                }

                existingAccounts.TryGetValue(company.Id ?? "", out var existing);

                var accountStatus = MapLifecycleToStatus(company.Properties.Lifecyclestage, company.Properties.HsLeadStatus);
                var (coverage, carrier, expiresAt) = ExtractDealFields(primaryDeal);

                if (existing == null)
                {
                    var account = new Account
                    {
                        AffinityId      = affinityId,
                        CompanyName     = company.Properties.Name,
                        HubSpotId       = company.Id,
                        City            = company.Properties.City,
                        State           = company.Properties.State,
                        AccountStatus   = accountStatus,
                        PrimaryCoverage = coverage,
                        PrimaryCarrier  = carrier,
                        PolicyExpiresAt = expiresAt,
                        PrimaryDealId   = primaryDeal?.Id,
                        LastSyncedAt    = now,
                    };
                    db.Accounts.Add(account);
                    existingAccounts[company.Id ?? ""] = account;
                }
                else
                {
                    existing.CompanyName     = company.Properties.Name;
                    existing.City            = company.Properties.City;
                    existing.State           = company.Properties.State;
                    existing.AccountStatus   = accountStatus;
                    existing.PrimaryCoverage = coverage;
                    existing.PrimaryCarrier  = carrier;
                    existing.PolicyExpiresAt = expiresAt;
                    existing.PrimaryDealId   = primaryDeal?.Id;
                    existing.LastSyncedAt    = now;
                    existing.UpdatedAt       = now;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing company {CompanyId} — skipping", company.Id);
                // continue to next company
            }
        }

        await db.SaveChangesAsync(ct);
        await RefreshOppCountsAsync(affinityId);

        // ADO#1053: Enhanced diagnostics logging
        var statusCounts = existingAccounts.Values
            .GroupBy(a => a.AccountStatus ?? "NULL")
            .ToDictionary(g => g.Key, g => g.Count());
        _logger.LogInformation("[AccountSync] Status distribution: {Counts}",
            string.Join(", ", statusCounts.Select(kv => $"{kv.Key}={kv.Value}")));

        // Log lifecycle stage distribution from source HubSpot data
        var lifecycleGroups = companies
            .GroupBy(c => c.Properties?.Lifecyclestage ?? "null")
            .Select(g => $"{g.Key}={g.Count()}");
        _logger.LogInformation("[AccountSync] Lifecycle stages from HubSpot: {Stages}",
            string.Join(", ", lifecycleGroups));

        // Log lead status distribution
        var leadStatusGroups = companies
            .GroupBy(c => c.Properties?.HsLeadStatus ?? "null")
            .Select(g => $"{g.Key}={g.Count()}");
        _logger.LogInformation("[AccountSync] Lead statuses from HubSpot: {Statuses}",
            string.Join(", ", leadStatusGroups));

        // Log how many accounts got deal data
        var withDealId = existingAccounts.Values.Count(a => !string.IsNullOrEmpty(a.PrimaryDealId));
        var withCoverage = existingAccounts.Values.Count(a => !string.IsNullOrEmpty(a.PrimaryCoverage));
        var withCarrier = existingAccounts.Values.Count(a => !string.IsNullOrEmpty(a.PrimaryCarrier));
        var withExpiration = existingAccounts.Values.Count(a => a.PolicyExpiresAt != null);
        _logger.LogInformation("[AccountSync] Deal data populated: {DealId} with deal ID, {Cov} with coverage, {Car} with carrier, {Exp} with expiration",
            withDealId, withCoverage, withCarrier, withExpiration);

        _logger.LogInformation("[AccountSync] Sync complete for {Aff}", affinityId);
    }

    // ── Fetch all companies ───────────────────────────────────────────────

    private async Task<List<HsCompany>> FetchAllCompaniesAsync(HttpClient client, CancellationToken ct)
    {
        var companies = new List<HsCompany>();
        string? after = null;

        // ADO#1016: Expanded properties to include lifecyclestage for status mapping
        const string props = "name,city,state,lifecyclestage,hs_lead_status";

        do
        {
            var url = $"/crm/v3/objects/companies?limit=100&properties={props}" +
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
            await Task.Delay(RateLimitDelayMs, ct);
        } while (after != null && companies.Count < 2000);

        return companies;
    }

    // ── Fetch all deals ───────────────────────────────────────────────────

    private async Task<List<HsDeal>> FetchAllDealsAsync(HttpClient client, CancellationToken ct)
    {
        var deals = new List<HsDeal>();
        string? after = null;

        // ADO#1053: Expanded property list with common insurance custom property names
        // Note: exact custom property names vary per HubSpot instance — we try multiple fallbacks
        const string props = "dealname,dealstage,closedate,amount," +
            "line_of_business,coverage_type,hs_line_of_business," +
            "coverage_lines,lines_of_business,insurance_type," +
            "carrier_name,carrier,insurance_carrier,primary_carrier," +
            "policy_expiration_date,expiration_date,policy_exp_date,renewal_date";

        do
        {
            var url = $"/crm/v3/objects/deals?limit=100&properties={props}" +
                (after != null ? $"&after={after}" : "");
            var resp = await client.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[AccountSync] HubSpot deals fetch failed: {Status}", resp.StatusCode);
                break;
            }

            var page = await resp.Content.ReadFromJsonAsync<HsDealPage>(Opts, ct);
            if (page?.Results != null)
                deals.AddRange(page.Results);

            after = page?.Paging?.Next?.After;
            await Task.Delay(RateLimitDelayMs, ct);
        } while (after != null && deals.Count < 5000);

        // ADO#1053: Debug logging for deal property coverage
        var withCoverage = deals.Count(d => !string.IsNullOrEmpty(
            d.Properties?.LineOfBusiness ?? d.Properties?.CoverageType ?? d.Properties?.HsLineOfBusiness
            ?? d.Properties?.CoverageLines ?? d.Properties?.LinesOfBusiness ?? d.Properties?.InsuranceType));
        var withCarrier = deals.Count(d => !string.IsNullOrEmpty(
            d.Properties?.CarrierName ?? d.Properties?.Carrier ?? d.Properties?.InsuranceCarrier ?? d.Properties?.PrimaryCarrierHs));
        var withExpiration = deals.Count(d => !string.IsNullOrEmpty(
            d.Properties?.PolicyExpirationDate ?? d.Properties?.ExpirationDate ?? d.Properties?.RenewalDate));
        _logger.LogInformation("[AccountSync] Deal property coverage: {Total} deals, {Cov} with coverage, {Car} with carrier, {Exp} with expiration",
            deals.Count, withCoverage, withCarrier, withExpiration);

        return deals;
    }

    // ── Fetch company→deal associations ───────────────────────────────────

    private async Task<Dictionary<string, List<string>>> FetchCompanyDealAssociationsAsync(
        HttpClient client, List<HsCompany> companies, CancellationToken ct)
    {
        var result = new Dictionary<string, List<string>>();

        // Process in batches to respect rate limits
        var companyIds = companies
            .Where(c => !string.IsNullOrEmpty(c.Id))
            .Select(c => c.Id!)
            .ToList();

        // HubSpot batch associations endpoint
        const int batchSize = 100;
        int totalAssociationsFound = 0;

        for (int i = 0; i < companyIds.Count; i += batchSize)
        {
            var batch = companyIds.Skip(i).Take(batchSize).ToList();
            var batchEnd = Math.Min(i + batchSize, companyIds.Count);

            var request = new { inputs = batch.Select(id => new { id }).ToList() };
            var resp = await client.PostAsJsonAsync(
                "/crm/v3/associations/companies/deals/batch/read",
                request, Opts, ct);

            if (resp.IsSuccessStatusCode)
            {
                var assocResult = await resp.Content.ReadFromJsonAsync<HsAssociationBatchResult>(Opts, ct);
                int batchMappings = 0;

                if (assocResult?.Results != null)
                {
                    foreach (var item in assocResult.Results)
                    {
                        if (item.From?.Id != null && item.To != null)
                        {
                            var dealIds = item.To
                                .Where(t => t.ToObjectId != null)
                                .Select(t => t.ToObjectId!)
                                .ToList();
                            if (dealIds.Any())
                            {
                                result[item.From.Id] = dealIds;
                                batchMappings++;
                            }
                        }
                    }
                }
                totalAssociationsFound += batchMappings;

                // ADO#1053: Log each batch result for debugging
                _logger.LogDebug("[AccountSync] Association batch {Start}-{End}: {Count} company→deal mappings found",
                    i, batchEnd, batchMappings);
            }
            else
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[AccountSync] Association batch {Start}-{End} failed: {Status} — {Body}",
                    i, batchEnd, resp.StatusCode, body.Length > 500 ? body[..500] : body);
            }

            await Task.Delay(RateLimitDelayMs, ct);
        }

        // ADO#1053: Summary logging
        _logger.LogInformation("[AccountSync] Association fetch complete: {Total} companies with deal associations out of {CompanyCount}",
            totalAssociationsFound, companyIds.Count);

        return result;
    }

    // ── Pick primary deal ─────────────────────────────────────────────────

    private HsDeal? PickPrimaryDeal(List<string> dealIds, Dictionary<string, HsDeal> dealLookup)
    {
        var matchedDeals = dealIds
            .Where(id => dealLookup.ContainsKey(id))
            .Select(id => dealLookup[id])
            .ToList();

        if (!matchedDeals.Any()) return null;

        // Prefer non-closed deals, then most recent by closedate
        var openDeals = matchedDeals
            .Where(d => !IsDealClosed(d.Properties?.Dealstage))
            .ToList();

        if (openDeals.Any())
        {
            return openDeals
                .OrderByDescending(d => ParseDate(d.Properties?.Closedate))
                .ThenByDescending(d => d.Id)
                .First();
        }

        // All closed — pick most recent
        return matchedDeals
            .OrderByDescending(d => ParseDate(d.Properties?.Closedate))
            .ThenByDescending(d => d.Id)
            .First();
    }

    private bool IsDealClosed(string? dealstage)
    {
        if (string.IsNullOrEmpty(dealstage)) return false;
        var stage = dealstage.ToLowerInvariant();
        return stage.Contains("closed") || stage.Contains("lost") || stage.Contains("won");
    }

    // ── Map lifecyclestage to AccountStatus ───────────────────────────────

    private string MapLifecycleToStatus(string? lifecyclestage, string? hsLeadStatus = null)
    {
        // Primary: use lifecyclestage
        if (!string.IsNullOrEmpty(lifecyclestage))
        {
            var mapped = lifecyclestage.ToLowerInvariant() switch
            {
                "customer"                => "Active",
                "lead"                    => "Prospect",
                "subscriber"              => "Prospect",
                "marketingqualifiedlead"  => "Prospect",
                "salesqualifiedlead"      => "Prospect",
                "opportunity"             => "Prospect",
                "evangelist"              => "Active",   // evangelists are happy customers
                "other"                   => "Inactive",
                _                         => (string?)null
            };
            if (mapped != null) return mapped;
        }

        // Secondary: check hs_lead_status for more specific info
        if (!string.IsNullOrEmpty(hsLeadStatus))
        {
            return hsLeadStatus.ToUpperInvariant() switch
            {
                "OPEN_DEAL"   => "Active",    // has an open deal = active relationship
                "CONNECTED"   => "Prospect",
                "IN_PROGRESS" => "Prospect",
                "OPEN"        => "Prospect",
                "NEW"         => "Prospect",
                "UNQUALIFIED" => "Inactive",
                "BAD_TIMING"  => "Inactive",
                _             => "Prospect"
            };
        }

        return "Inactive";
    }

    // ── Extract deal fields ───────────────────────────────────────────────

    private (string? coverage, string? carrier, DateTime? expiresAt) ExtractDealFields(HsDeal? deal)
    {
        if (deal?.Properties == null)
            return (null, null, null);

        var props = deal.Properties;

        // ADO#1053: Coverage — expanded fallback chain
        var coverage = props.LineOfBusiness
            ?? props.CoverageType
            ?? props.HsLineOfBusiness
            ?? props.CoverageLines
            ?? props.LinesOfBusiness
            ?? props.InsuranceType;

        // ADO#1053: Carrier — expanded fallback chain
        var carrier = props.CarrierName
            ?? props.Carrier
            ?? props.InsuranceCarrier
            ?? props.PrimaryCarrierHs;

        // ADO#1053: Expiration — expanded fallback chain
        // Note: closedate is the deal won/close date — NOT the policy expiration date, so it is not used as a fallback
        var expiresAt = ParseDate(props.PolicyExpirationDate)
            ?? ParseDate(props.ExpirationDate)
            ?? ParseDate(props.PolicyExpDate)
            ?? ParseDate(props.RenewalDate);

        return (coverage, carrier, expiresAt);
    }

    private DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
        if (DateTime.TryParse(dateStr, out var dt)) return dt;
        // HubSpot sometimes returns Unix milliseconds
        if (long.TryParse(dateStr, out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        return null;
    }

    // ── HubSpot DTOs ──────────────────────────────────────────────────────

    private class HsCompanyPage
    {
        public List<HsCompany>? Results { get; set; }
        public HsPaging?        Paging  { get; set; }
    }

    private class HsCompany
    {
        public string?         Id         { get; set; }
        public HsCompanyProps? Properties { get; set; }
    }

    private class HsCompanyProps
    {
        public string? Name           { get; set; }
        public string? City           { get; set; }
        public string? State          { get; set; }
        public string? Lifecyclestage { get; set; }
        public string? HsLeadStatus   { get; set; }
    }

    private class HsDealPage
    {
        public List<HsDeal>? Results { get; set; }
        public HsPaging?     Paging  { get; set; }
    }

    private class HsDeal
    {
        public string?      Id         { get; set; }
        public HsDealProps? Properties { get; set; }
    }

    private class HsDealProps
    {
        public string? Dealname              { get; set; }
        public string? Dealstage             { get; set; }
        public string? Closedate             { get; set; }
        public string? Amount                { get; set; }

        // ADO#1053: Coverage line — expanded fallback property names
        [JsonPropertyName("line_of_business")]
        public string? LineOfBusiness        { get; set; }
        [JsonPropertyName("coverage_type")]
        public string? CoverageType          { get; set; }
        [JsonPropertyName("hs_line_of_business")]
        public string? HsLineOfBusiness      { get; set; }
        [JsonPropertyName("coverage_lines")]
        public string? CoverageLines         { get; set; }
        [JsonPropertyName("lines_of_business")]
        public string? LinesOfBusiness       { get; set; }
        [JsonPropertyName("insurance_type")]
        public string? InsuranceType         { get; set; }

        // ADO#1053: Carrier — expanded fallback property names
        [JsonPropertyName("carrier_name")]
        public string? CarrierName           { get; set; }
        public string? Carrier               { get; set; }
        [JsonPropertyName("insurance_carrier")]
        public string? InsuranceCarrier      { get; set; }
        [JsonPropertyName("primary_carrier")]
        public string? PrimaryCarrierHs      { get; set; }

        // ADO#1053: Expiration — expanded fallback property names
        [JsonPropertyName("policy_expiration_date")]
        public string? PolicyExpirationDate  { get; set; }
        [JsonPropertyName("expiration_date")]
        public string? ExpirationDate        { get; set; }
        [JsonPropertyName("policy_exp_date")]
        public string? PolicyExpDate         { get; set; }
        [JsonPropertyName("renewal_date")]
        public string? RenewalDate           { get; set; }
    }

    private class HsPaging
    {
        public HsPagingNext? Next { get; set; }
    }

    private class HsPagingNext
    {
        public string? After { get; set; }
    }

    private class HsAssociationBatchResult
    {
        public List<HsAssociationItem>? Results { get; set; }
    }

    private class HsAssociationItem
    {
        public HsAssociationFrom?       From { get; set; }
        public List<HsAssociationTo>?   To   { get; set; }
    }

    private class HsAssociationFrom
    {
        public string? Id { get; set; }
    }

    private class HsAssociationTo
    {
        // ADO#1053: Explicit JSON property name to ensure deserialization works
        // HubSpot API returns "toObjectId" in camelCase
        [JsonPropertyName("toObjectId")]
        public string? ToObjectId { get; set; }
    }
}
