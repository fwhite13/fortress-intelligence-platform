using FortressNexus.Web.Models.DTOs;
using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

/// <summary>
/// Phase 2 — live ADO creation via the ADO REST API.
/// Predecessor resolution and batch ordering are pre-wired; API calls are TODO.
/// </summary>
public class AdoCreationService : IAdoService
{
    private readonly ILogger<AdoCreationService> _logger;

    public AdoCreationService(ILogger<AdoCreationService> logger)
    {
        _logger = logger;
    }

    public Task<List<AdoProcessTemplate>> GetProcessTemplatesAsync(string organization)
    {
        throw new NotImplementedException("Phase 2 — use StubAdoService for Phase 1");
    }

    public Task<List<string>> GetProjectsAsync(string organization)
    {
        throw new NotImplementedException("Phase 2 — use StubAdoService for Phase 1");
    }

    public Task<string> CreateProjectAsync(string organization, string projectName, string processTemplateTypeId)
    {
        throw new NotImplementedException("Phase 2 — use StubAdoService for Phase 1");
    }

    public Task<WorkItemRecord> CreateWorkItemAsync(ArtifactSet artifactSet, AdoWorkItemDto dto)
    {
        throw new NotImplementedException("Phase 2 — use StubAdoService for Phase 1");
    }

    public async Task<List<WorkItemRecord>> CreateWorkItemBatchAsync(ArtifactSet artifactSet, List<AdoWorkItemDto> items)
    {
        _logger.LogInformation("[AdoCreationService] CreateWorkItemBatchAsync: {Count} items", items.Count);

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

        // Step 2: Create WIs one at a time via ADO API, building title→ID map as we go
        var titleToAdoId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var records = new List<WorkItemRecord>();

        foreach (var dto in orderedItems)
        {
            // TODO Phase 2: call ADO REST API to create WI, get back real ADO ID
            int createdAdoId = 0; // placeholder — replaced by API response
            string createdAdoUrl = ""; // placeholder

            var record = new WorkItemRecord
            {
                ArtifactSetId = artifactSet.Id,
                AdoWorkItemId = createdAdoId,
                AdoWorkItemUrl = createdAdoUrl,
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

            records.Add(record);
            titleToAdoId[record.Title] = createdAdoId;

            // Step 3: Predecessor resolution — link immediately after creation
            foreach (var predecessorTitle in dto.PredecessorTitles ?? [])
            {
                if (titleToAdoId.TryGetValue(predecessorTitle, out int predecessorAdoId))
                {
                    // TODO Phase 2: call ADO API to add predecessor link
                    // Link type: System.LinkTypes.Dependency-Reverse (successor on this WI → predecessor on target)
                    _logger.LogInformation(
                        "Predecessor '{PredTitle}' resolved to ID {PredId} for WI '{WiTitle}'",
                        predecessorTitle, predecessorAdoId, record.Title);
                }
                else
                {
                    _logger.LogWarning(
                        "Predecessor '{PredTitle}' could not be resolved for WI '{WiTitle}'",
                        predecessorTitle, record.Title);
                    // TODO Phase 2: add ADO comment via API
                    await AddCommentAsync(createdAdoId,
                        $"Predecessor '{predecessorTitle}' could not be auto-linked — please add manually.");
                }
            }
        }

        artifactSet.ExternalDependencyCount = records.Count(w => w.IsExternalDependency);

        return records;
    }

    private Task AddCommentAsync(int workItemId, string comment)
    {
        // TODO Phase 2: call ADO REST API to add comment to work item
        _logger.LogInformation("[AdoCreationService] AddComment to WI {WiId}: {Comment}", workItemId, comment);
        return Task.CompletedTask;
    }
}
