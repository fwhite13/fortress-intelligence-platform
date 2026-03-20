using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public interface IHubSpotService
{
    Task SyncLifecycleAsync(Guid opportunityId, LifecycleStage stage);
    Task SyncBoundAsync(Guid opportunityId, PolicyShadowRecord shadow);
    Task SyncOwnerAsync(Guid opportunityId, string newOwnerUserId);
    Task SyncClosedAsync(Guid opportunityId, CloseReason reason);
}

public class HubSpotServiceStub : IHubSpotService
{
    private readonly ILogger<HubSpotServiceStub> _logger;
    public HubSpotServiceStub(ILogger<HubSpotServiceStub> logger) => _logger = logger;

    public Task SyncLifecycleAsync(Guid opportunityId, LifecycleStage stage)
    {
        _logger.LogInformation("[HubSpot stub] Lifecycle sync: {Id} → {Stage}", opportunityId, stage);
        return Task.CompletedTask;
    }

    public Task SyncBoundAsync(Guid opportunityId, PolicyShadowRecord shadow)
    {
        _logger.LogInformation("[HubSpot stub] Policy shadow: {Id}", shadow.Id);
        return Task.CompletedTask;
    }

    public Task SyncOwnerAsync(Guid opportunityId, string newOwnerUserId)
    {
        _logger.LogInformation("[HubSpot stub] Owner sync: {Id} → {Owner}", opportunityId, newOwnerUserId);
        return Task.CompletedTask;
    }

    public Task SyncClosedAsync(Guid opportunityId, CloseReason reason)
    {
        _logger.LogInformation("[HubSpot stub] Closed sync: {Id} — {Reason}", opportunityId, reason);
        return Task.CompletedTask;
    }
}
