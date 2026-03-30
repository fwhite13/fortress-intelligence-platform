using FortressNexus.Web.Models.DTOs;

namespace FortressNexus.Web.Services;

public class ArtifactGenerationService : IArtifactGenerationService
{
    private readonly ILogger<ArtifactGenerationService> _logger;

    public ArtifactGenerationService(ILogger<ArtifactGenerationService> logger)
    {
        _logger = logger;
    }

    public Task<List<AdoWorkItemDto>> GenerateWorkItemsAsync(int specDocumentId) =>
        throw new NotImplementedException("WI-6");
}
