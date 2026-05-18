using System.Net;
using System.Text.Json;
using FortressAI.Web.Services.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FortressAI.Web.Services;

/// <summary>
/// Internal MCP adapter for Azure DevOps REST tools.
/// Mirrors the BraveSearchMcpAdapter pattern exactly.
///
/// Endpoint: POST /internal/mcp/devops
/// Auth:     Loopback-only (same-process HttpClient calls from McpToolService).
///           The caller passes the userId as X-API-Key so this adapter can
///           look up the user's stored PAT via DevOpsToolService.
///
/// BraveSearch wiring reference:
///   - Registered in DatabaseInitializationService (same INSERT pattern as Brave row)
///   - McpToolService.ExecuteToolAsync routes devops__<tool> → this endpoint via McpHttpTransport
///   - GetConversationToolsAsync loads tools from mcp_servers table (devops slug)
///   - GetActiveServersForUserAsync filters devops server: only visible when user is connected
/// </summary>
[ApiController]
public class DevOpsMcpAdapter : ControllerBase
{
    private readonly DevOpsToolService _devOpsSvc;
    private readonly ILogger<DevOpsMcpAdapter> _logger;

    // Tool manifest — 12 Azure DevOps tools (6 read + 6 write)
    private static readonly string DevOpsToolManifest = JsonSerializer.Serialize(new object[]
    {
        new
        {
            name = "list_devops_projects",
            description = "List all Azure DevOps projects in the user's organization",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {},
              ""required"": []
            }").RootElement
        },
        new
        {
            name = "get_work_item",
            description = "Get details of a specific Azure DevOps work item by ID",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""id"": { ""type"": ""integer"", ""description"": ""Work item ID"" }
              },
              ""required"": [""id""]
            }").RootElement
        },
        new
        {
            name = "query_work_items",
            description = "Query Azure DevOps work items using WIQL (Work Item Query Language) or a natural language description. For natural language, describe what you want (e.g. 'my open bugs'). For WIQL, start with SELECT.",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""wiql"": { ""type"": ""string"", ""description"": ""WIQL query string (starting with SELECT) or natural language description of what to find"" },
                ""project"": { ""type"": ""string"", ""description"": ""Azure DevOps project name (optional — defaults to first project in org)"" }
              },
              ""required"": [""wiql""]
            }").RootElement
        },
        new
        {
            name = "list_repositories",
            description = "List Git repositories in an Azure DevOps project",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""project"": { ""type"": ""string"", ""description"": ""Azure DevOps project name"" }
              },
              ""required"": [""project""]
            }").RootElement
        },
        new
        {
            name = "list_pipelines",
            description = "List build/release pipelines in an Azure DevOps project",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""project"": { ""type"": ""string"", ""description"": ""Azure DevOps project name"" }
              },
              ""required"": [""project""]
            }").RootElement
        },
        new
        {
            name = "trigger_pipeline",
            description = "Trigger a pipeline run in Azure DevOps",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""project"": { ""type"": ""string"", ""description"": ""Azure DevOps project name"" },
                ""pipeline_id"": { ""type"": ""integer"", ""description"": ""Pipeline ID to trigger"" },
                ""parameters"": {
                  ""type"": ""object"",
                  ""description"": ""Optional key/value pipeline variables to pass to the run"",
                  ""additionalProperties"": { ""type"": ""string"" }
                }
              },
              ""required"": [""project"", ""pipeline_id""]
            }").RootElement
        },
        new
        {
            name = "create_work_item",
            description = "Create a new work item in Azure DevOps. Params: {project: string, type: string, title: string, description?: string, assignedTo?: string, areaPath?: string, iterationPath?: string, tags?: string}",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""project"": { ""type"": ""string"" },
                ""type"": { ""type"": ""string"", ""description"": ""Work item type e.g. Task, Bug, User Story"" },
                ""title"": { ""type"": ""string"" },
                ""description"": { ""type"": ""string"" },
                ""assignedTo"": { ""type"": ""string"" },
                ""areaPath"": { ""type"": ""string"" },
                ""iterationPath"": { ""type"": ""string"" },
                ""tags"": { ""type"": ""string"" }
              },
              ""required"": [""project"", ""type"", ""title""]
            }").RootElement
        },
        new
        {
            name = "update_work_item",
            description = "Update fields on an existing work item by ID. Params: {id: number, state?: string, title?: string, description?: string, assignedTo?: string, priority?: number, tags?: string}",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""id"": { ""type"": ""integer"" },
                ""state"": { ""type"": ""string"" },
                ""title"": { ""type"": ""string"" },
                ""description"": { ""type"": ""string"" },
                ""assignedTo"": { ""type"": ""string"" },
                ""priority"": { ""type"": ""integer"" },
                ""tags"": { ""type"": ""string"" }
              },
              ""required"": [""id""]
            }").RootElement
        },
        new
        {
            name = "add_work_item_comment",
            description = "Add a comment to a work item. Params: {project: string, id: number, comment: string}",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""project"": { ""type"": ""string"" },
                ""id"": { ""type"": ""integer"" },
                ""comment"": { ""type"": ""string"" }
              },
              ""required"": [""project"", ""id"", ""comment""]
            }").RootElement
        },
        new
        {
            name = "create_branch",
            description = "Create a new branch from a base ref in a repository. Params: {project: string, repo: string, branchName: string, baseBranch?: string}",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""project"": { ""type"": ""string"" },
                ""repo"": { ""type"": ""string"" },
                ""branchName"": { ""type"": ""string"" },
                ""baseBranch"": { ""type"": ""string"", ""default"": ""main"" }
              },
              ""required"": [""project"", ""repo"", ""branchName""]
            }").RootElement
        },
        new
        {
            name = "create_pull_request",
            description = "Create a pull request. Params: {project: string, repo: string, title: string, sourceBranch: string, targetBranch: string, description?: string, reviewers?: string[]}",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""project"": { ""type"": ""string"" },
                ""repo"": { ""type"": ""string"" },
                ""title"": { ""type"": ""string"" },
                ""sourceBranch"": { ""type"": ""string"" },
                ""targetBranch"": { ""type"": ""string"" },
                ""description"": { ""type"": ""string"" },
                ""reviewers"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
              },
              ""required"": [""project"", ""repo"", ""title"", ""sourceBranch"", ""targetBranch""]
            }").RootElement
        },
        new
        {
            name = "update_pull_request",
            description = "Update a pull request — complete, abandon, add reviewers, or update description. Params: {project: string, repo: string, pullRequestId: number, status?: string, description?: string, reviewers?: string[]}",
            inputSchema = JsonDocument.Parse(@"{
              ""type"": ""object"",
              ""properties"": {
                ""project"": { ""type"": ""string"" },
                ""repo"": { ""type"": ""string"" },
                ""pullRequestId"": { ""type"": ""integer"" },
                ""status"": { ""type"": ""string"", ""enum"": [""active"", ""completed"", ""abandoned""] },
                ""description"": { ""type"": ""string"" },
                ""reviewers"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
              },
              ""required"": [""project"", ""repo"", ""pullRequestId""]
            }").RootElement
        }
    });

    public DevOpsMcpAdapter(DevOpsToolService devOpsSvc, ILogger<DevOpsMcpAdapter> logger)
    {
        _devOpsSvc = devOpsSvc;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("/internal/mcp/devops")]
    public async Task<IActionResult> HandleMcpRequest([FromBody] McpCallRequest request)
    {
        // Restrict to loopback only — internal same-process endpoint (mirrors BraveSearchMcpAdapter).
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();
        if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
            return StatusCode(403, new { error = new { code = 403, message = "Forbidden: internal endpoint" } });

        if (request.Method == "tools/list")
        {
            return Ok(new
            {
                jsonrpc = "2.0",
                id = request.Id,
                result = new { tools = JsonDocument.Parse(DevOpsToolManifest).RootElement }
            });
        }

        if (request.Method != "tools/call")
            return BadRequest(new { error = new { code = -32601, message = "Method not found" } });

        // Extract userId from X-API-Key header (McpToolService passes the userId string as the api_key)
        var userIdStr = HttpContext.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(new { error = new { code = 401, message = "Invalid or missing user identity" } });
        }

        var toolName = request.Params?.Name ?? "";
        var args = request.Params?.Arguments ?? default;

        _logger.LogInformation("[DevOps] Tool dispatch: userId={UserId} tool={Tool}", userId, toolName);

        try
        {
            string? result = toolName switch
            {
                "list_devops_projects" => await _devOpsSvc.ListProjectsAsync(userId),

                "get_work_item" => await HandleGetWorkItem(userId, args),

                "query_work_items" => await HandleQueryWorkItems(userId, args),

                "list_repositories" => await HandleListRepositories(userId, args),

                "list_pipelines" => await HandleListPipelines(userId, args),

                "trigger_pipeline" => await HandleTriggerPipeline(userId, args),

                "create_work_item" => await HandleCreateWorkItem(userId, args),

                "update_work_item" => await HandleUpdateWorkItem(userId, args),

                "add_work_item_comment" => await HandleAddWorkItemComment(userId, args),

                "create_branch" => await HandleCreateBranch(userId, args),

                "create_pull_request" => await HandleCreatePullRequest(userId, args),

                "update_pull_request" => await HandleUpdatePullRequest(userId, args),

                _ => null
            };

            _logger.LogInformation("[DevOps] Tool result: userId={UserId} tool={Tool} resultNull={IsNull}", userId, toolName, result is null);

            if (result is null && toolName is
                "list_devops_projects" or "get_work_item" or "query_work_items" or
                "list_repositories" or "list_pipelines" or "trigger_pipeline" or
                "create_work_item" or "update_work_item" or "add_work_item_comment" or
                "create_branch" or "create_pull_request" or "update_pull_request")
            {
                _logger.LogWarning("[DevOps] Null result for userId={UserId} tool={Tool} — likely no connection or credential failure", userId, toolName);
                return Ok(new McpCallResponse
                {
                    Jsonrpc = "2.0",
                    Id = request.Id,
                    Result = new McpToolResultContent
                    {
                        Content = new List<McpContentBlock>
                        {
                            new McpContentBlock { Type = "text", Text = $"Azure DevOps tool '{toolName}' returned no result. Your ADO connection may be missing or your PAT may have expired. Please reconnect in Settings → Integrations → Azure DevOps." }
                        },
                        IsError = true
                    }
                });
            }

            if (result is null)
                return BadRequest(new { error = new { code = -32601, message = $"Unknown tool: {toolName}" } });

            return Ok(new McpCallResponse
            {
                Jsonrpc = "2.0",
                Id = request.Id,
                Result = new McpToolResultContent
                {
                    Content = new List<McpContentBlock>
                    {
                        new McpContentBlock { Type = "text", Text = result }
                    },
                    IsError = false
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DevOps] Tool call failed: {ToolName}", toolName);
            return Ok(new McpCallResponse
            {
                Jsonrpc = "2.0",
                Id = request.Id,
                Result = new McpToolResultContent
                {
                    Content = new List<McpContentBlock>
                    {
                        new McpContentBlock { Type = "text", Text = $"Azure DevOps tool error: {ex.Message}" }
                    },
                    IsError = true
                }
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Argument extractors
    // ─────────────────────────────────────────────────────────────────

    private Task<string?> HandleGetWorkItem(Guid userId, JsonElement args)
    {
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return Task.FromResult<string?>("Missing required parameter: id (integer)");
        return _devOpsSvc.GetWorkItemAsync(userId, idEl.GetInt32());
    }

    private Task<string?> HandleQueryWorkItems(Guid userId, JsonElement args)
    {
        var wiql = args.TryGetProperty("wiql", out var w) ? w.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(wiql))
            return Task.FromResult<string?>("Missing required parameter: wiql");

        var project = args.TryGetProperty("project", out var p) ? p.GetString() : null;
        return _devOpsSvc.QueryWorkItemsAsync(userId, wiql, project);
    }

    private Task<string?> HandleListRepositories(Guid userId, JsonElement args)
    {
        var project = args.TryGetProperty("project", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult<string?>("Missing required parameter: project");
        return _devOpsSvc.ListRepositoriesAsync(userId, project);
    }

    private Task<string?> HandleListPipelines(Guid userId, JsonElement args)
    {
        var project = args.TryGetProperty("project", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult<string?>("Missing required parameter: project");
        return _devOpsSvc.ListPipelinesAsync(userId, project);
    }

    private Task<string?> HandleTriggerPipeline(Guid userId, JsonElement args)
    {
        var project = args.TryGetProperty("project", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult<string?>("Missing required parameter: project");

        if (!args.TryGetProperty("pipeline_id", out var pidEl) || pidEl.ValueKind != JsonValueKind.Number)
            return Task.FromResult<string?>("Missing required parameter: pipeline_id (integer)");

        var pipelineId = pidEl.GetInt32();

        // Optional parameters dict
        Dictionary<string, string>? parameters = null;
        if (args.TryGetProperty("parameters", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Object)
        {
            parameters = new Dictionary<string, string>();
            foreach (var prop in paramsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    parameters[prop.Name] = prop.Value.GetString() ?? "";
            }
        }

        return _devOpsSvc.TriggerPipelineRunAsync(userId, project, pipelineId, parameters);
    }

    private Task<string?> HandleCreateWorkItem(Guid userId, JsonElement args)
    {
        var project = args.TryGetProperty("project", out var proj) ? proj.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult<string?>("Missing required parameter: project");

        var type = args.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(type))
            return Task.FromResult<string?>("Missing required parameter: type");

        var title = args.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(title))
            return Task.FromResult<string?>("Missing required parameter: title");

        var description = args.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
        var assignedTo = args.TryGetProperty("assignedTo", out var atEl) ? atEl.GetString() : null;
        var areaPath = args.TryGetProperty("areaPath", out var apEl) ? apEl.GetString() : null;
        var iterationPath = args.TryGetProperty("iterationPath", out var ipEl) ? ipEl.GetString() : null;
        var tags = args.TryGetProperty("tags", out var tagsEl) ? tagsEl.GetString() : null;

        return _devOpsSvc.CreateWorkItemAsync(userId, project, type, title, description, assignedTo, areaPath, iterationPath, tags);
    }

    private Task<string?> HandleUpdateWorkItem(Guid userId, JsonElement args)
    {
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return Task.FromResult<string?>("Missing required parameter: id (integer)");

        var id = idEl.GetInt32();
        var state = args.TryGetProperty("state", out var stEl) ? stEl.GetString() : null;
        var title = args.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
        var description = args.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
        var assignedTo = args.TryGetProperty("assignedTo", out var atEl) ? atEl.GetString() : null;
        int? priority = args.TryGetProperty("priority", out var prEl) && prEl.ValueKind == JsonValueKind.Number
            ? prEl.GetInt32() : null;
        var tags = args.TryGetProperty("tags", out var tagsEl) ? tagsEl.GetString() : null;

        return _devOpsSvc.UpdateWorkItemAsync(userId, id, state, title, description, assignedTo, priority, tags);
    }

    private Task<string?> HandleAddWorkItemComment(Guid userId, JsonElement args)
    {
        var project = args.TryGetProperty("project", out var proj) ? proj.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult<string?>("Missing required parameter: project");

        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return Task.FromResult<string?>("Missing required parameter: id (integer)");

        var comment = args.TryGetProperty("comment", out var commentEl) ? commentEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(comment))
            return Task.FromResult<string?>("Missing required parameter: comment");

        return _devOpsSvc.AddWorkItemCommentAsync(userId, project, idEl.GetInt32(), comment);
    }

    private Task<string?> HandleCreateBranch(Guid userId, JsonElement args)
    {
        var project = args.TryGetProperty("project", out var proj) ? proj.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult<string?>("Missing required parameter: project");

        var repo = args.TryGetProperty("repo", out var repoEl) ? repoEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(repo))
            return Task.FromResult<string?>("Missing required parameter: repo");

        var branchName = args.TryGetProperty("branchName", out var bnEl) ? bnEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(branchName))
            return Task.FromResult<string?>("Missing required parameter: branchName");

        var baseBranch = args.TryGetProperty("baseBranch", out var bbEl) ? bbEl.GetString() ?? "main" : "main";

        return _devOpsSvc.CreateBranchAsync(userId, project, repo, branchName, baseBranch);
    }

    private Task<string?> HandleCreatePullRequest(Guid userId, JsonElement args)
    {
        var project = args.TryGetProperty("project", out var proj) ? proj.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult<string?>("Missing required parameter: project");

        var repo = args.TryGetProperty("repo", out var repoEl) ? repoEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(repo))
            return Task.FromResult<string?>("Missing required parameter: repo");

        var title = args.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(title))
            return Task.FromResult<string?>("Missing required parameter: title");

        var sourceBranch = args.TryGetProperty("sourceBranch", out var sbEl) ? sbEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(sourceBranch))
            return Task.FromResult<string?>("Missing required parameter: sourceBranch");

        var targetBranch = args.TryGetProperty("targetBranch", out var tbEl) ? tbEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(targetBranch))
            return Task.FromResult<string?>("Missing required parameter: targetBranch");

        var description = args.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;

        List<string>? reviewers = null;
        if (args.TryGetProperty("reviewers", out var revEl) && revEl.ValueKind == JsonValueKind.Array)
        {
            reviewers = revEl.EnumerateArray()
                .Where(r => r.ValueKind == JsonValueKind.String)
                .Select(r => r.GetString()!)
                .ToList();
        }

        return _devOpsSvc.CreatePullRequestAsync(userId, project, repo, title, sourceBranch, targetBranch, description, reviewers);
    }

    private Task<string?> HandleUpdatePullRequest(Guid userId, JsonElement args)
    {
        var project = args.TryGetProperty("project", out var proj) ? proj.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult<string?>("Missing required parameter: project");

        var repo = args.TryGetProperty("repo", out var repoEl) ? repoEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(repo))
            return Task.FromResult<string?>("Missing required parameter: repo");

        if (!args.TryGetProperty("pullRequestId", out var prIdEl) || prIdEl.ValueKind != JsonValueKind.Number)
            return Task.FromResult<string?>("Missing required parameter: pullRequestId (integer)");

        var pullRequestId = prIdEl.GetInt32();
        var status = args.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
        var description = args.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;

        List<string>? reviewers = null;
        if (args.TryGetProperty("reviewers", out var revEl) && revEl.ValueKind == JsonValueKind.Array)
        {
            reviewers = revEl.EnumerateArray()
                .Where(r => r.ValueKind == JsonValueKind.String)
                .Select(r => r.GetString()!)
                .ToList();
        }

        return _devOpsSvc.UpdatePullRequestAsync(userId, project, repo, pullRequestId, status, description, reviewers);
    }
}
