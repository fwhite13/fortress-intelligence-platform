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
            Description = dto.Description,
            Status = "Created",
            WiTemplate = dto.WiTemplate,
            IsExternalDependency = dto.IsExternalDependency,
            ExternalOwner = dto.ExternalOwner,
            TestedByTitles = dto.TestedByTitles,
            ParentTitle = dto.ParentTitle,
            PredecessorTitles = dto.PredecessorTitles
        };
        return Task.FromResult(record);
    }

    public Task<List<WorkItemRecord>> CreateWorkItemBatchAsync(ArtifactSet artifactSet, List<AdoWorkItemDto> items, string callerUpn)
    {
        _logger.LogInformation("[StubAdoService] CreateWorkItemBatchAsync: {Count} items", items.Count);
        _logger.LogInformation("[StubAdoService] CreateWorkItemBatchAsync items: {ItemsJson}",
            System.Text.Json.JsonSerializer.Serialize(items));

        // Step 1: Sort DTOs — Epics first, then Features, Stories, Tasks, Test Cases
        var orderedItems = items
            .OrderBy(w => w.WorkItemType switch {
                "Epic" => 0,
                "Feature" => 1,
                "User Story" => 2,
                "Task" => 3,
                "Test Case" => 4,
                _ => 5
            })
            .ToList();

        // Step 2: Create records (two-pass: create all, then resolve predecessors)
        var records = orderedItems.Select(dto => new WorkItemRecord
        {
            ArtifactSetId = artifactSet.Id,
            AdoWorkItemId = Random.Shared.Next(1000, 9999),
            AdoWorkItemUrl = $"https://dev.azure.com/stub/_workitems/edit/{Random.Shared.Next(1000, 9999)}",
            WorkItemType = dto.WorkItemType,
            Title = dto.Title,
            Description = dto.Description,
            Status = "Created",
            WiTemplate = dto.WiTemplate,
            IsExternalDependency = dto.IsExternalDependency,
            ExternalOwner = dto.ExternalOwner,
            TestedByTitles = dto.TestedByTitles,
            ParentTitle = dto.ParentTitle,
            PredecessorTitles = dto.PredecessorTitles
        }).ToList();

        // Step 3: Build title→ID map from all created records
        var titleToAdoId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            titleToAdoId[record.Title] = record.AdoWorkItemId.GetValueOrDefault();
        }

        // Step 4: Predecessor resolution pass
        foreach (var record in records)
        {
            foreach (var predecessorTitle in record.PredecessorTitles ?? [])
            {
                if (titleToAdoId.TryGetValue(predecessorTitle, out int predecessorAdoId))
                {
                    _logger.LogInformation(
                        "Predecessor '{PredTitle}' resolved to ID {PredId} for WI '{WiTitle}'",
                        predecessorTitle, predecessorAdoId, record.Title);
                }
                else
                {
                    _logger.LogWarning(
                        "UNRESOLVED PREDECESSOR: '{PredTitle}' could not be resolved for WI '{WiTitle}' (ADO ID {AdoId})",
                        predecessorTitle, record.Title, record.AdoWorkItemId);
                }
            }
        }

        artifactSet.ExternalDependencyCount = records.Count(w => w.IsExternalDependency);

        return Task.FromResult(records);
    }
}
