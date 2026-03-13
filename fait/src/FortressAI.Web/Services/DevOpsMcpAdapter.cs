using System.Net;
using System.Text.Json;
using FortressAI.Web.Services.Mcp;
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

    // Tool manifest — 6 Azure DevOps tools
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
        }
    });

    public DevOpsMcpAdapter(DevOpsToolService devOpsSvc, ILogger<DevOpsMcpAdapter> logger)
    {
        _devOpsSvc = devOpsSvc;
        _logger = logger;
    }

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

                _ => null
            };

            if (result is null && toolName is
                "list_devops_projects" or "get_work_item" or "query_work_items" or
                "list_repositories" or "list_pipelines" or "trigger_pipeline")
            {
                result = "No result returned. The user may not have an Azure DevOps connection configured, or the request failed. Ask the user to check their Azure DevOps settings.";
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
}
