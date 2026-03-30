using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

public class SpecGenerationService : ISpecGenerationService
{
    private readonly ILogger<SpecGenerationService> _logger;

    public SpecGenerationService(ILogger<SpecGenerationService> logger)
    {
        _logger = logger;
    }

    public Task<SpecDocument> GenerateAsync(int submissionId) =>
        throw new NotImplementedException("WI-3");

    public Task<SpecDocument> RegenerateAsync(int specDocumentId) =>
        throw new NotImplementedException("WI-3");
}
