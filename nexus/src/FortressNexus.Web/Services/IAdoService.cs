using FortressNexus.Web.Models.DTOs;
using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

public interface IAdoService
{
    Task<List<AdoProcessTemplate>> GetProcessTemplatesAsync(string organization);
    Task<List<string>> GetProjectsAsync(string organization);
    Task<string> CreateProjectAsync(string organization, string projectName, string processTemplateTypeId);
    Task<WorkItemRecord> CreateWorkItemAsync(ArtifactSet artifactSet, AdoWorkItemDto dto);
    Task<List<WorkItemRecord>> CreateWorkItemBatchAsync(ArtifactSet artifactSet, List<AdoWorkItemDto> items, string callerUpn);
}
