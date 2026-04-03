using FortressNexus.Web.Models.DTOs;
using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

/// <summary>
/// Phase 1 stub — logs all ADO calls and returns mock data.
/// Real implementation in WI-7.
/// </summary>
public class StubAdoService : IAdoService
{
    private readonly ILogger<StubAdoService> _logger;

    public StubAdoService(ILogger<StubAdoService> logger)
    {
        _logger = logger;
    }

    public Task<List<AdoProcessTemplate>> GetProcessTemplatesAsync(string organization)
    {
        _logger.LogInformation("[StubAdoService] GetProcessTemplatesAsync called for org: {Org}", organization);
        var templates = new List<AdoProcessTemplate>
        {
            new("adcc42ab-9882-485e-a3ed-7678f01f66bc", "Agile", "Microsoft Agile process template"),
            new("6b724908-ef14-45cf-84f8-768b5384da45", "Scrum", "Microsoft Scrum process template"),
            new("27450541-8e31-4150-9947-dc59f998fc01", "CMMI", "Microsoft CMMI process template")
        };
        return Task.FromResult(templates);
    }

    public Task<List<string>> GetProjectsAsync(string organization)
    {
        _logger.LogInformation("[StubAdoService] GetProjectsAsync called for org: {Org}", organization);
        return Task.FromResult(new List<string> { "FAIT", "FIRM", "FORMS", "NEXUS" });
    }

    public Task<string> CreateProjectAsync(string organization, string projectName, string processTemplateTypeId)
    {
        _logger.LogInformation("[StubAdoService] CreateProjectAsync: org={Org}, project={Project}, template={Template}",
            organization, projectName, processTemplateTypeId);
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task<WorkItemRecord> CreateWorkItemAsync(ArtifactSet artifactSet, AdoWorkItemDto dto)
    {
        _logger.LogInformation("[StubAdoService] CreateWorkItemAsync: type={Type}, title={Title}",
            dto.WorkItemType, dto.Title);
        var record = new WorkItemRecord
        {
            ArtifactSetId = artifactSet.Id,
            AdoWorkItemId = Random.Shared.Next(1000, 9999),
            AdoWorkItemUrl = $"https://dev.azure.com/stub/_workitems/edit/{Random.Shared.Next(1000, 9999)}",
            WorkItemType = dto.WorkItemType,
            Title = dto.Title,
            Status = "Created"
        };
        return Task.FromResult(record);
    }

    public Task<List<WorkItemRecord>> CreateWorkItemBatchAsync(ArtifactSet artifactSet, List<AdoWorkItemDto> items)
    {
        _logger.LogInformation("[StubAdoService] CreateWorkItemBatchAsync: {Count} items", items.Count);
        _logger.LogInformation("[StubAdoService] CreateWorkItemBatchAsync items: {ItemsJson}",
            System.Text.Json.JsonSerializer.Serialize(items));
        var records = items.Select(dto => new WorkItemRecord
        {
            ArtifactSetId = artifactSet.Id,
            AdoWorkItemId = Random.Shared.Next(1000, 9999),
            AdoWorkItemUrl = $"https://dev.azure.com/stub/_workitems/edit/{Random.Shared.Next(1000, 9999)}",
            WorkItemType = dto.WorkItemType,
            Title = dto.Title,
            Status = "Created"
        }).ToList();
        return Task.FromResult(records);
    }
}
