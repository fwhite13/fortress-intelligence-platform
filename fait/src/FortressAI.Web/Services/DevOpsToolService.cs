using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FortressAI.Web.Services;

/// <summary>
/// Wraps Azure DevOps REST API calls using the per-user PAT stored in DevOpsConnectionService.
/// All methods return null (not throw) when the user has no DevOps connection or on error.
/// </summary>
public class DevOpsToolService
{
    private readonly DevOpsConnectionService _devOpsConn;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DevOpsToolService> _logger;

    public DevOpsToolService(
        DevOpsConnectionService devOpsConn,
        IHttpClientFactory httpClientFactory,
        ILogger<DevOpsToolService> logger)
    {
        _devOpsConn = devOpsConn;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────
    // Auth helper
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an HttpRequestMessage with Basic auth using the Azure DevOps PAT convention:
    ///   Authorization: Basic base64(":{PAT}")
    /// </summary>
    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string pat)
    {
        var req = new HttpRequestMessage(method, url);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{pat}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        req.Headers.Add("Accept", "application/json");
        return req;
    }

    /// <summary>
    /// Resolves orgUrl + PAT for the user. Returns null if not connected.
    /// Never logs the PAT value.
    /// </summary>
    private async Task<(string orgUrl, string pat)?> GetCredentialsAsync(Guid userId)
    {
        var orgUrl = await _devOpsConn.GetOrgUrlAsync(userId);
        if (string.IsNullOrEmpty(orgUrl)) return null;

        var pat = await _devOpsConn.GetDecryptedPatAsync(userId);
        if (string.IsNullOrEmpty(pat)) return null;

        return (orgUrl, pat);
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: list_devops_projects
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists all Azure DevOps projects in the user's organization.
    /// GET {orgUrl}/_apis/projects?api-version=7.1
    /// </summary>
    public async Task<string?> ListProjectsAsync(Guid userId)
    {
        var creds = await GetCredentialsAsync(userId);
        if (creds is null) return null;
        var (orgUrl, pat) = creds.Value;

        try
        {
            var url = $"{orgUrl}/_apis/projects?api-version=7.1";
            using var req = BuildRequest(HttpMethod.Get, url, pat);
            var http = _httpClientFactory.CreateClient("azure-devops");
            using var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var sb = new StringBuilder();
            sb.AppendLine("Azure DevOps Projects:");
            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var proj in values.EnumerateArray())
                {
                    var name = proj.TryGetProperty("name", out var n) ? n.GetString() : "(unknown)";
                    var id = proj.TryGetProperty("id", out var i) ? i.GetString() : "";
                    var state = proj.TryGetProperty("state", out var s) ? s.GetString() : "";
                    sb.AppendLine($"- {name} (ID: {id}, State: {state})");
                }
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DevOps] ListProjectsAsync failed for user {UserId}", userId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: get_work_item
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets details of a specific Azure DevOps work item by ID.
    /// GET {orgUrl}/_apis/wit/workitems/{id}?api-version=7.1
    /// </summary>
    public async Task<string?> GetWorkItemAsync(Guid userId, int id)
    {
        var creds = await GetCredentialsAsync(userId);
        if (creds is null) return null;
        var (orgUrl, pat) = creds.Value;

        try
        {
            var url = $"{orgUrl}/_apis/wit/workitems/{id}?$expand=all&api-version=7.1";
            using var req = BuildRequest(HttpMethod.Get, url, pat);
            var http = _httpClientFactory.CreateClient("azure-devops");
            using var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return FormatWorkItem(doc.RootElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DevOps] GetWorkItemAsync(id={Id}) failed for user {UserId}", id, userId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: query_work_items
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Queries Azure DevOps work items using WIQL.
    /// Step 1: POST {orgUrl}/{project}/_apis/wit/wiql?api-version=7.1 → get IDs
    /// Step 2: GET {orgUrl}/_apis/wit/workitems?ids=1,2,3&api-version=7.1 → get details
    /// </summary>
    public async Task<string?> QueryWorkItemsAsync(Guid userId, string wiqlOrQuery, string? project = null)
    {
        var creds = await GetCredentialsAsync(userId);
        if (creds is null) return null;
        var (orgUrl, pat) = creds.Value;

        try
        {
            // Resolve project: use provided, or fall back to first project
            var resolvedProject = project;
            if (string.IsNullOrEmpty(resolvedProject))
            {
                resolvedProject = await GetFirstProjectNameAsync(orgUrl, pat);
            }
            if (string.IsNullOrEmpty(resolvedProject))
            {
                return "Could not determine a project to query. Please specify a project name.";
            }

            // Normalize: if the input doesn't look like WIQL, use a default query
            var wiql = LooksLikeWiql(wiqlOrQuery)
                ? wiqlOrQuery
                : BuildDefaultWiql(wiqlOrQuery);

            var wiqlUrl = $"{orgUrl}/{Uri.EscapeDataString(resolvedProject)}/_apis/wit/wiql?api-version=7.1";
            var wiqlBody = JsonSerializer.Serialize(new { query = wiql });

            using var wiqlReq = BuildRequest(HttpMethod.Post, wiqlUrl, pat);
            wiqlReq.Content = new StringContent(wiqlBody, Encoding.UTF8, "application/json");

            var http = _httpClientFactory.CreateClient("azure-devops");
            using var wiqlResp = await http.SendAsync(wiqlReq);
            wiqlResp.EnsureSuccessStatusCode();

            using var wiqlDoc = JsonDocument.Parse(await wiqlResp.Content.ReadAsStringAsync());

            // Extract work item IDs from WIQL response
            var ids = new List<int>();
            if (wiqlDoc.RootElement.TryGetProperty("workItems", out var workItemsEl))
            {
                foreach (var wi in workItemsEl.EnumerateArray())
                {
                    if (wi.TryGetProperty("id", out var idEl))
                        ids.Add(idEl.GetInt32());
                }
            }

            if (!ids.Any())
                return $"No work items found in project '{resolvedProject}' matching the query.";

            // Fetch up to 50 work item details in one call
            var topIds = ids.Take(50).ToList();
            var idsParam = string.Join(",", topIds);
            var detailUrl = $"{orgUrl}/_apis/wit/workitems?ids={idsParam}&$expand=fields&api-version=7.1";

            using var detailReq = BuildRequest(HttpMethod.Get, detailUrl, pat);
            using var detailResp = await http.SendAsync(detailReq);
            detailResp.EnsureSuccessStatusCode();

            using var detailDoc = JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync());

            var sb = new StringBuilder();
            sb.AppendLine($"Work Items in '{resolvedProject}' ({topIds.Count} of {ids.Count} shown):");
            sb.AppendLine();

            if (detailDoc.RootElement.TryGetProperty("value", out var detailValues))
            {
                foreach (var wi in detailValues.EnumerateArray())
                {
                    sb.AppendLine(FormatWorkItem(wi));
                    sb.AppendLine();
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DevOps] QueryWorkItemsAsync failed for user {UserId}", userId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: list_repositories
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists Git repositories in an Azure DevOps project.
    /// GET {orgUrl}/{project}/_apis/git/repositories?api-version=7.1
    /// </summary>
    public async Task<string?> ListRepositoriesAsync(Guid userId, string project)
    {
        var creds = await GetCredentialsAsync(userId);
        if (creds is null) return null;
        var (orgUrl, pat) = creds.Value;

        try
        {
            var url = $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/git/repositories?api-version=7.1";
            using var req = BuildRequest(HttpMethod.Get, url, pat);
            var http = _httpClientFactory.CreateClient("azure-devops");
            using var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var sb = new StringBuilder();
            sb.AppendLine($"Git Repositories in '{project}':");

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var repo in values.EnumerateArray())
                {
                    var name = repo.TryGetProperty("name", out var n) ? n.GetString() : "(unknown)";
                    var cloneUrl = repo.TryGetProperty("remoteUrl", out var r) ? r.GetString() : "";
                    var defaultBranch = repo.TryGetProperty("defaultBranch", out var b) ? b.GetString()?.Replace("refs/heads/", "") : "";
                    sb.AppendLine($"- {name}");
                    if (!string.IsNullOrEmpty(cloneUrl)) sb.AppendLine($"  Clone URL: {cloneUrl}");
                    if (!string.IsNullOrEmpty(defaultBranch)) sb.AppendLine($"  Default branch: {defaultBranch}");
                }
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DevOps] ListRepositoriesAsync(project={Project}) failed for user {UserId}", project, userId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: list_pipelines
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists build/release pipelines in an Azure DevOps project.
    /// GET {orgUrl}/{project}/_apis/pipelines?api-version=7.1
    /// </summary>
    public async Task<string?> ListPipelinesAsync(Guid userId, string project)
    {
        var creds = await GetCredentialsAsync(userId);
        if (creds is null) return null;
        var (orgUrl, pat) = creds.Value;

        try
        {
            var url = $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/pipelines?api-version=7.1";
            using var req = BuildRequest(HttpMethod.Get, url, pat);
            var http = _httpClientFactory.CreateClient("azure-devops");
            using var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var sb = new StringBuilder();
            sb.AppendLine($"Pipelines in '{project}':");

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var pipeline in values.EnumerateArray())
                {
                    var name = pipeline.TryGetProperty("name", out var n) ? n.GetString() : "(unknown)";
                    var id = pipeline.TryGetProperty("id", out var i) ? i.GetInt32().ToString() : "";
                    var folder = pipeline.TryGetProperty("folder", out var f) ? f.GetString() : "";
                    sb.AppendLine($"- {name} (ID: {id}{(string.IsNullOrEmpty(folder) || folder == "\\" ? "" : $", Folder: {folder}")})");
                }
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DevOps] ListPipelinesAsync(project={Project}) failed for user {UserId}", project, userId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: trigger_pipeline
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers a pipeline run in Azure DevOps.
    /// POST {orgUrl}/{project}/_apis/pipelines/{pipelineId}/runs?api-version=7.1
    /// </summary>
    public async Task<string?> TriggerPipelineRunAsync(
        Guid userId,
        string project,
        int pipelineId,
        Dictionary<string, string>? parameters = null)
    {
        var creds = await GetCredentialsAsync(userId);
        if (creds is null) return null;
        var (orgUrl, pat) = creds.Value;

        try
        {
            var url = $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/pipelines/{pipelineId}/runs?api-version=7.1";

            // Build run request body
            object body;
            if (parameters?.Any() == true)
            {
                var variables = parameters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)new { value = kvp.Value });
                body = new { resources = new { }, variables };
            }
            else
            {
                body = new { resources = new { }, variables = new { } };
            }

            using var req = BuildRequest(HttpMethod.Post, url, pat);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var http = _httpClientFactory.CreateClient("azure-devops");
            using var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var runId = doc.RootElement.TryGetProperty("id", out var rid) ? rid.GetInt32().ToString() : "?";
            var state = doc.RootElement.TryGetProperty("state", out var st) ? st.GetString() : "unknown";

            return $"Pipeline run triggered successfully.\nRun ID: {runId}\nState: {state}\nProject: {project}\nPipeline ID: {pipelineId}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DevOps] TriggerPipelineRunAsync(project={Project}, pipelineId={PipelineId}) failed for user {UserId}",
                project, pipelineId, userId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the name of the first project in the organization, or null if none found.
    /// Used as the default project when the caller doesn't specify one.
    /// </summary>
    private async Task<string?> GetFirstProjectNameAsync(string orgUrl, string pat)
    {
        try
        {
            var url = $"{orgUrl}/_apis/projects?$top=1&api-version=7.1";
            using var req = BuildRequest(HttpMethod.Get, url, pat);
            var http = _httpClientFactory.CreateClient("azure-devops");
            using var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var proj in values.EnumerateArray())
                {
                    if (proj.TryGetProperty("name", out var n))
                        return n.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DevOps] GetFirstProjectNameAsync failed");
        }
        return null;
    }

    /// <summary>
    /// Formats a single work item JSON element into a human-readable string.
    /// </summary>
    private static string FormatWorkItem(JsonElement wi)
    {
        var id = wi.TryGetProperty("id", out var idEl) ? idEl.GetInt32().ToString() : "?";
        var fields = wi.TryGetProperty("fields", out var f) ? (JsonElement?)f : null;

        var title = GetField(fields, "System.Title") ?? "(no title)";
        var state = GetField(fields, "System.State") ?? "";
        var assignedTo = GetAssignedTo(fields);
        var workItemType = GetField(fields, "System.WorkItemType") ?? "";
        var description = GetField(fields, "System.Description");
        var areaPath = GetField(fields, "System.AreaPath") ?? "";
        var iterationPath = GetField(fields, "System.IterationPath") ?? "";
        var priority = GetField(fields, "Microsoft.VSTS.Common.Priority");
        var changedDate = GetField(fields, "System.ChangedDate");

        var sb = new StringBuilder();
        sb.AppendLine($"Work Item #{id}: {title}");
        sb.AppendLine($"  Type: {workItemType}  |  State: {state}");
        if (!string.IsNullOrEmpty(assignedTo)) sb.AppendLine($"  Assigned To: {assignedTo}");
        if (!string.IsNullOrEmpty(priority)) sb.AppendLine($"  Priority: {priority}");
        if (!string.IsNullOrEmpty(areaPath)) sb.AppendLine($"  Area: {areaPath}");
        if (!string.IsNullOrEmpty(iterationPath)) sb.AppendLine($"  Iteration: {iterationPath}");
        if (!string.IsNullOrEmpty(changedDate)) sb.AppendLine($"  Last Changed: {changedDate}");
        if (!string.IsNullOrEmpty(description))
        {
            // Strip HTML tags for readability
            var cleanDesc = System.Text.RegularExpressions.Regex.Replace(description, "<[^>]+>", "").Trim();
            if (!string.IsNullOrEmpty(cleanDesc))
                sb.AppendLine($"  Description: {(cleanDesc.Length > 200 ? cleanDesc[..200] + "..." : cleanDesc)}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string? GetField(JsonElement? fields, string fieldName)
    {
        if (fields is null) return null;
        if (fields.Value.TryGetProperty(fieldName, out var val))
        {
            if (val.ValueKind == JsonValueKind.String) return val.GetString();
            if (val.ValueKind == JsonValueKind.Number) return val.GetRawText();
        }
        return null;
    }

    private static string? GetAssignedTo(JsonElement? fields)
    {
        if (fields is null) return null;
        if (!fields.Value.TryGetProperty("System.AssignedTo", out var assignedTo)) return null;
        if (assignedTo.ValueKind == JsonValueKind.String) return assignedTo.GetString();
        // AssignedTo can be an object with displayName
        if (assignedTo.ValueKind == JsonValueKind.Object &&
            assignedTo.TryGetProperty("displayName", out var dn))
            return dn.GetString();
        return null;
    }

    /// <summary>
    /// Heuristic check — does the input look like a WIQL query?
    /// WIQL starts with SELECT or is an empty string (use default).
    /// </summary>
    private static bool LooksLikeWiql(string input)
    {
        var trimmed = input.TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a default WIQL query for open work items assigned to @Me.
    /// The natural-language input is noted but not yet parsed — Bedrock handles
    /// translation before calling this tool. Falls back to the default WIQL.
    /// </summary>
    private static string BuildDefaultWiql(string naturalLanguageHint)
    {
        // Default: open work items assigned to the current user, most recently changed first
        return """
            SELECT [System.Id], [System.Title], [System.State], [System.AssignedTo], [System.WorkItemType]
            FROM WorkItems
            WHERE [System.AssignedTo] = @Me
            AND [System.State] <> 'Closed'
            AND [System.State] <> 'Resolved'
            ORDER BY [System.ChangedDate] DESC
            """;
    }
}
