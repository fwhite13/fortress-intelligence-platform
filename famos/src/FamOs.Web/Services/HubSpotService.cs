using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

/// <summary>
/// Real HubSpot deal-stage sync.
/// Matches on opportunity ID stored in custom property famos_opportunity_id.
/// Phase 1: one-directional only (FAM OS → HubSpot).
/// </summary>
public class HubSpotService : IHubSpotService
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration     _config;
    private readonly ILogger<HubSpotService> _logger;

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    // HubSpot pipeline stage IDs — override per affinity group in appsettings if needed
    private static readonly Dictionary<LifecycleStage, string> StageMap = new()
    {
        [LifecycleStage.Intake]           = "appointmentscheduled",
        [LifecycleStage.UnderwritingPrep] = "qualifiedtobuy",
        [LifecycleStage.Marketed]         = "presentationscheduled",
        [LifecycleStage.QuotesReceived]   = "decisionmakerboughtin",
        [LifecycleStage.ClientDecision]   = "contractsent",
        [LifecycleStage.Binding]          = "contractsent",
        [LifecycleStage.Bound]            = "closedwon",
        [LifecycleStage.ClosedNotBound]   = "closedlost",
    };

    public HubSpotService(IHttpClientFactory factory, IConfiguration config,
        ILogger<HubSpotService> logger)
    {
        _factory = factory;
        _config  = config;
        _logger  = logger;
    }

    private string? ServiceKey => _config["HubSpot:ServiceKey"];

    public async Task SyncLifecycleAsync(Guid opportunityId, LifecycleStage stage)
    {
        if (string.IsNullOrEmpty(ServiceKey))
        {
            _logger.LogDebug("[HubSpot] ServiceKey not configured — skipping sync for {Id}", opportunityId);
            return;
        }

        try
        {
            var client = _factory.CreateClient("HubSpot");
            var dealId = await FindDealByOpportunityIdAsync(client, opportunityId);

            if (dealId == null)
            {
                _logger.LogWarning("[HubSpot] No deal found for opportunity {Id} — skipping stage sync", opportunityId);
                return;
            }

            if (!StageMap.TryGetValue(stage, out var hsStage))
            {
                _logger.LogWarning("[HubSpot] No stage mapping for {Stage}", stage);
                return;
            }

            var props = new Dictionary<string, object>
            {
                ["dealstage"] = hsStage
            };

            if (stage == LifecycleStage.ClosedNotBound)
            {
                props["closedate"]                  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                props["hs_deal_stage_probability"]  = 0;
            }
            else if (stage == LifecycleStage.Bound)
            {
                props["closedate"]                  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                props["hs_deal_stage_probability"]  = 1;
            }

            await PatchDealAsync(client, dealId, props);
            _logger.LogInformation("[HubSpot] Deal {DealId} → {Stage}", dealId, hsStage);
        }
        catch (Exception ex)
        {
            // Non-fatal: log and continue. Never fail a lifecycle transition because of HubSpot.
            _logger.LogError(ex, "[HubSpot] SyncLifecycle failed for {Id}", opportunityId);
        }
    }

    public async Task SyncBoundAsync(Guid opportunityId, PolicyShadowRecord shadow)
    {
        if (string.IsNullOrEmpty(ServiceKey)) return;

        try
        {
            var client = _factory.CreateClient("HubSpot");
            var dealId = await FindDealByOpportunityIdAsync(client, opportunityId);
            if (dealId == null) return;

            var props = new Dictionary<string, object>
            {
                ["dealstage"]                  = "closedwon",
                ["closedate"]                  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["hs_deal_stage_probability"]  = 1,
            };
            if (shadow.PremiumAmount.HasValue)
                props["amount"] = shadow.PremiumAmount.Value;

            await PatchDealAsync(client, dealId, props);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HubSpot] SyncBound failed for {Id}", opportunityId);
        }
    }

    // ── HubSpot API helpers ────────────────────────────────────────────────

    /// <summary>
    /// Search HubSpot deals by the FAM OS opportunity ID stored as a custom property
    /// famos_opportunity_id.
    /// </summary>
    private async Task<string?> FindDealByOpportunityIdAsync(HttpClient client, Guid opportunityId)
    {
        var searchBody = new
        {
            filterGroups = new[]
            {
                new
                {
                    filters = new[]
                    {
                        new
                        {
                            propertyName = "famos_opportunity_id",
                            @operator    = "EQ",
                            value        = opportunityId.ToString()
                        }
                    }
                }
            },
            properties = new[] { "dealname", "dealstage" },
            limit = 1
        };

        var resp = await client.PostAsJsonAsync(
            "/crm/v3/objects/deals/search", searchBody, Opts);

        if (resp.IsSuccessStatusCode)
        {
            var result = await resp.Content.ReadFromJsonAsync<HsSearchResult>(Opts);
            if (result?.Results?.Length > 0)
                return result.Results[0].Id;
        }

        _logger.LogDebug("[HubSpot] No deal with famos_opportunity_id={Id}", opportunityId);
        return null;
    }

    private async Task PatchDealAsync(HttpClient client, string dealId,
        Dictionary<string, object> props)
    {
        var body    = new { properties = props };
        var content = new StringContent(
            JsonSerializer.Serialize(body, Opts),
            System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PatchAsync($"/crm/v3/objects/deals/{dealId}", content);
        resp.EnsureSuccessStatusCode();
    }

    // ── Response DTOs ─────────────────────────────────────────────────────

    private class HsSearchResult
    {
        public HsDeal[]? Results { get; set; }
    }

    private class HsDeal
    {
        public string Id { get; set; } = "";
    }
}
