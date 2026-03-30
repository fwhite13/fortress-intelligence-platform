using FortressNexus.Web.Models.DTOs;

namespace FortressNexus.Web.Services;

public interface IArtifactGenerationService
{
    Task<List<AdoWorkItemDto>> GenerateWorkItemsAsync(int specDocumentId);
}
