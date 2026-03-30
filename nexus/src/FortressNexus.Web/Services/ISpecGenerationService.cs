using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

public interface ISpecGenerationService
{
    Task<SpecDocument> GenerateAsync(int submissionId);
    Task<SpecDocument> RegenerateAsync(int specDocumentId);
}
