using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

public class MockupProcessingService : IMockupProcessingService
{
    private readonly ILogger<MockupProcessingService> _logger;

    public MockupProcessingService(ILogger<MockupProcessingService> logger)
    {
        _logger = logger;
    }

    public Task<string> ExtractTextAsync(UploadedFile file) =>
        throw new NotImplementedException("WI-4");
}
