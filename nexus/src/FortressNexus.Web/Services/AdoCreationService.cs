using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FortressNexus.Web.Models.DTOs;
using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

/// <summary>
/// Live ADO creation service — posts work items to Azure DevOps via the ADO REST API.
/// Uses per-user PAT stored encrypted via DataProtection.
/// </summary>
public class AdoCreationService : IAdoService
{
    private readonly ILogger<AdoCreationService> _logger;
    private readonly IAdoCredentialService _credentialService;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public AdoCreationService(
        ILogger<AdoCreationService> logger,
        IAdoCredentialService credentialService,
        IConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _credentialService = credentialService;
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<AdoProcessTemplate>> GetProcessTemplatesAsync(string organization)
    {
        // Resolve caller UPN — not needed here but kept for interface consistency
        var url = $"https://dev.azure.com/{organization}/_apis/process/processes?api-version=7.1";
        // We don't have a per-call UPN here; return stub list to avoid breaking callers
        // (This method is not exercised in the current UI flow)
        _logger.LogInformation("[AdoCreationService] GetProcessTemplatesAsync for org: {Org}", organization);
        await Task.CompletedTask;
        return new List<AdoProcessTemplate>
        {
            new("adcc42ab-9882-485e-a3ed-7678f01f66bc", "Agile", "Microsoft Agile process template"),
            new("6b724908-ef14-45cf-84f8-768b5384da45", "Scrum", "Microsoft Scrum process template"),
            new("27450541-8e31-4150-9947-dc59f998fc01", "CMMI", "Microsoft CMMI process template")
        };
    }

    public async Task<List<string>> GetProjectsAsync(string organization)
    {
        // Not called from NexusArtifacts anymore (it calls IAdoCredentialService directly)
        // Kept for IAdoService interface compliance
        _logger.LogInformation("[AdoCreationService] GetProjectsAsync for org: {Org}", organization);
        await Task.CompletedTask;
        return new List<string>();
    }

    public Task<string> CreateProjectAsync(string organization, string projectName, string processTemplateTypeId)
    {
        throw new NotSupportedException("Project creation is not supported in AdoCreationService.");
    }

    public Task<WorkItemRecord> CreateWorkItemAsync(ArtifactSet artifactSet, AdoWorkItemDto dto)
    {
        throw new NotSupportedException("Use CreateWorkItemBatchAsync for ADO work item creation.");
    }

    public async Task<List<WorkItemRecord>> CreateWorkItemBatchAsync(ArtifactSet artifactSet, List<AdoWorkItemDto> items)
    {
        _logger.LogInformation("[AdoCreationService] CreateWorkItemBatchAsync: {Count} items for {Project}",
            items.Count, artifactSet.AdoProjectName);

        // Step 1: Get caller's PAT
        var pat = await _credentialService.GetDecryptedPatAsync(artifactSet.CreatedBy)
            ?? throw new InvalidOperationException($"No ADO credential found for {artifactSet.CreatedBy}. Please add your PAT in the project selector.");

        var project = artifactSet.AdoProjectName;
        var org = _config["Nexus:Ado:Organization"] ?? "FortressAffinityGroup";

        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = BuildBasicAuth(pat);

        // Step 2: Filter out external dependencies — these require action outside ADO
        var externalCount = items.Count(w => w.IsExternalDependency);
        if (externalCount > 0)
            _logger.LogInformation("[AdoCreationService] Skipping {Count} external dependency WIs — not posted to ADO", externalCount);

        // Sort DTOs — Epics first, then Features, Stories, Tasks, Test Cases
        var orderedItems = items
            .Where(w => !w.IsExternalDependency)
            .OrderBy(w => w.WorkItemType switch
            {
                "Epic" => 0,
                "Feature" => 1,
                "User Story" => 2,
                "Task" => 3,
                "Test Case" => 4,
                _ => 5
            })
            .ToList();

        // Step 3: Create WIs one at a time via ADO API, building title→ID map as we go
        var titleToAdoId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var records = new List<WorkItemRecord>();

        foreach (var dto in orderedItems)
        {
            int createdAdoId;
            string createdAdoUrl;

            try
            {
                (createdAdoId, createdAdoUrl) = await CreateSingleWorkItemAsync(client, org, project, dto);
                _logger.LogInformation("[AdoCreationService] Created WI: {Type} '{Title}' → ADO#{Id}",
                    dto.WorkItemType, dto.Title, createdAdoId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AdoCreationService] Failed to create WI: {Type} '{Title}'",
                    dto.WorkItemType, dto.Title);
                var errorRecord = new WorkItemRecord
                {
                    ArtifactSetId = artifactSet.Id,
                    AdoWorkItemId = null,
                    AdoWorkItemUrl = null,
                    WorkItemType = dto.WorkItemType,
                    Title = dto.Title,
                    Description = dto.Description,
                    Status = "Error",
                    ErrorDetail = ex.Message,
                    WiTemplate = dto.WiTemplate,
                    IsExternalDependency = dto.IsExternalDependency,
                    ExternalOwner = dto.ExternalOwner,
                    TestedByTitles = dto.TestedByTitles,
                    ParentTitle = dto.ParentTitle,
                    PredecessorTitles = dto.PredecessorTitles
                };
                records.Add(errorRecord);
                continue;
            }

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

            // Step 4: Parent linking — immediately after create
            if (!string.IsNullOrEmpty(dto.ParentTitle) && titleToAdoId.TryGetValue(dto.ParentTitle, out int parentAdoId))
            {
                try
                {
                    await AddRelationAsync(client, org, project, createdAdoId,
                        "System.LinkTypes.Hierarchy-Reverse", parentAdoId);
                    _logger.LogInformation("[AdoCreationService] Linked {ChildId} → parent {ParentId} ({ParentTitle})",
                        createdAdoId, parentAdoId, dto.ParentTitle);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AdoCreationService] Failed to link parent '{ParentTitle}' for WI #{Id}",
                        dto.ParentTitle, createdAdoId);
                }
            }

            // Step 5: Predecessor linking
            foreach (var predecessorTitle in dto.PredecessorTitles ?? [])
            {
                if (titleToAdoId.TryGetValue(predecessorTitle, out int predecessorAdoId))
                {
                    try
                    {
                        await AddRelationAsync(client, org, project, createdAdoId,
                            "System.LinkTypes.Dependency-Reverse", predecessorAdoId);
                        _logger.LogInformation("[AdoCreationService] Predecessor '{PredTitle}' (#{PredId}) linked to WI #{WiId}",
                            predecessorTitle, predecessorAdoId, createdAdoId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[AdoCreationService] Failed to link predecessor '{PredTitle}' for WI #{Id}",
                            predecessorTitle, createdAdoId);
                        await AddCommentAsync(client, org, project, createdAdoId,
                            $"Predecessor '{predecessorTitle}' could not be auto-linked — please add manually.");
                    }
                }
                else
                {
                    _logger.LogWarning("[AdoCreationService] Predecessor '{PredTitle}' could not be resolved for WI '{WiTitle}'",
                        predecessorTitle, record.Title);
                    await AddCommentAsync(client, org, project, createdAdoId,
                        $"Predecessor '{predecessorTitle}' could not be auto-linked — please add manually.");
                }
            }
        }

        // Step 6: "Tested By" linking (Test Cases → User Stories) — post-creation pass
        var tcMap = records
            .Where(r => r.WorkItemType == "Test Case" && r.AdoWorkItemId.HasValue)
            .ToDictionary(r => r.Title, r => r.AdoWorkItemId!.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var record in records.Where(r => r.WorkItemType == "User Story" && r.AdoWorkItemId.HasValue))
        {
            var dto = orderedItems.FirstOrDefault(d => string.Equals(d.Title, record.Title, StringComparison.OrdinalIgnoreCase));
            if (dto?.TestedByTitles is null) continue;

            foreach (var tcTitle in dto.TestedByTitles)
            {
                if (tcMap.TryGetValue(tcTitle, out int tcAdoId))
                {
                    try
                    {
                        await AddRelationAsync(client, org, project, record.AdoWorkItemId!.Value,
                            "Microsoft.VSTS.Common.TestedBy-Forward", tcAdoId);
                        _logger.LogInformation("[AdoCreationService] TestedBy link: Story #{StoryId} → TC #{TcId} ({TcTitle})",
                            record.AdoWorkItemId, tcAdoId, tcTitle);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[AdoCreationService] Failed to link TestedBy '{TcTitle}' for Story #{Id}",
                            tcTitle, record.AdoWorkItemId);
                    }
                }
            }
        }

        artifactSet.ExternalDependencyCount = records.Count(w => w.IsExternalDependency);
        return records;
    }

    // ── Private ADO REST helpers ──

    private async Task<(int Id, string Url)> CreateSingleWorkItemAsync(
        HttpClient client, string org, string project, AdoWorkItemDto dto)
    {
        var typeEncoded = Uri.EscapeDataString(dto.WorkItemType);
        var url = $"https://dev.azure.com/{org}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/${typeEncoded}?api-version=7.1";

        var ops = new List<object>
        {
            new { op = "add", path = "/fields/System.Title", value = dto.Title },
        };

        if (!string.IsNullOrEmpty(dto.Description))
            ops.Add(new { op = "add", path = "/fields/System.Description", value = dto.Description });

        if (!string.IsNullOrEmpty(dto.AcceptanceCriteria))
            ops.Add(new { op = "add", path = "/fields/Microsoft.VSTS.Common.AcceptanceCriteria", value = dto.AcceptanceCriteria });

        if (dto.Tags?.Count > 0)
            ops.Add(new { op = "add", path = "/fields/System.Tags", value = string.Join("; ", dto.Tags) });

        // Priority: 2 for all WIs
        ops.Add(new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = 2 });

        if (dto.WorkItemType == "User Story")
        {
            var points = dto.StoryPoints ?? 3;
            ops.Add(new { op = "add", path = "/fields/Microsoft.VSTS.Scheduling.StoryPoints", value = points });
        }

        if (dto.WorkItemType == "Task")
        {
            ops.Add(new { op = "add", path = "/fields/Microsoft.VSTS.Common.Activity", value = "Development" });
        }

        var json = JsonSerializer.Serialize(ops);
        var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        var response = await client.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"ADO API returned {(int)response.StatusCode} creating '{dto.Title}': {body}");

        using var doc = JsonDocument.Parse(body);
        var id = doc.RootElement.GetProperty("id").GetInt32();
        var wiUrl = doc.RootElement.TryGetProperty("_links", out var links)
            && links.TryGetProperty("html", out var html)
            && html.TryGetProperty("href", out var href)
            ? href.GetString() ?? ""
            : $"https://dev.azure.com/{org}/_workitems/edit/{id}";

        return (id, wiUrl);
    }

    private async Task AddRelationAsync(
        HttpClient client, string org, string project,
        int workItemId, string relType, int targetId)
    {
        var url = $"https://dev.azure.com/{org}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{workItemId}?api-version=7.1";

        var ops = new[]
        {
            new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = relType,
                    url = $"https://dev.azure.com/{org}/_apis/wit/workitems/{targetId}"
                }
            }
        };

        var json = JsonSerializer.Serialize(ops);
        var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
        var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"ADO API returned {(int)response.StatusCode} adding relation '{relType}' on WI {workItemId}: {body}");
        }
    }

    private async Task AddCommentAsync(HttpClient client, string org, string project, int workItemId, string comment)
    {
        var url = $"https://dev.azure.com/{org}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{workItemId}/comments?api-version=7.1-preview.3";

        var payload = JsonSerializer.Serialize(new { text = comment });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[AdoCreationService] AddComment on WI {Id} returned {Status}: {Body}",
                    workItemId, (int)response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdoCreationService] AddComment on WI {Id} threw exception", workItemId);
        }
    }

    private static AuthenticationHeaderValue BuildBasicAuth(string pat)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }
}
