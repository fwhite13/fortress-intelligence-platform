using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public interface IAmsService
{
    Task PushPolicyShadowAsync(PolicyShadowRecord record);
}

public class AmsServiceStub : IAmsService
{
    private readonly ILogger<AmsServiceStub> _logger;
    public AmsServiceStub(ILogger<AmsServiceStub> logger) => _logger = logger;

    public Task PushPolicyShadowAsync(PolicyShadowRecord record)
    {
        _logger.LogInformation("[AMS stub] Policy shadow push: {Id}", record.Id);
        return Task.CompletedTask;
    }
}
