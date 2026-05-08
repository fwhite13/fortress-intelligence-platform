using FortressNexus.Web.Models.DTOs;
using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

public interface IArtifactGenerationService
{
    Task<List<AdoWorkItemDto>> GenerateWorkItemsAsync(int specDocumentId);
    Task<ArtifactSet> DecomposeAndPersistAsync(int submissionId, int specDocumentId, string callerUpn, string adoProjectName);
}
