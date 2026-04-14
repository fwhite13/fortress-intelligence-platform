using System.Text.Json;
using FortressNexus.Web.Data;
using FortressNexus.Web.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace FortressNexus.Web.Services;

public class ArtifactGenerationService : IArtifactGenerationService
{
    private readonly NexusDbContext _db;
    private readonly BedrockService _bedrock;
    private readonly IConfiguration _config;
    private readonly ILogger<ArtifactGenerationService> _logger;

    public ArtifactGenerationService(
        NexusDbContext db,
        BedrockService bedrock,
        IConfiguration config,
        ILogger<ArtifactGenerationService> logger)
    {
        _db = db;
        _bedrock = bedrock;
        _config = config;
        _logger = logger;
    }

    public async Task<List<AdoWorkItemDto>> GenerateWorkItemsAsync(int specDocumentId)
    {
        _logger.LogInformation("[WI_GEN] Starting work item generation for SpecDocument {SpecDocumentId}", specDocumentId);

        var specDoc = await _db.SpecDocuments.FirstOrDefaultAsync(s => s.Id == specDocumentId);
        if (specDoc is null)
        {
            _logger.LogWarning("[WI_GEN] SpecDocument {SpecDocumentId} not found", specDocumentId);
            return new List<AdoWorkItemDto>();
        }

        var systemPrompt = _config["Nexus:Prompts:ArtifactGenSystem"]
            ?? "Decompose the following software specification into Azure DevOps User Stories. Return ONLY a valid JSON array.";

        var resolvedModelId = _config["FortressAI:ModelId"] ?? "us.anthropic.claude-sonnet-4-5-20250929-v1:0";
        var specContent = specDoc.EditedContent ?? specDoc.Content;

        try
        {
            var (text, promptTokens, completionTokens) = await _bedrock.InvokeAsync(systemPrompt, specContent, 8192, resolvedModelId);

            _logger.LogInformation("[WI_GEN] Bedrock response received for SpecDocument {SpecDocumentId}: {PromptTokens} prompt + {CompletionTokens} completion tokens",
                specDocumentId, promptTokens, completionTokens);

            var items = ParseWorkItems(text, specDocumentId);

            _logger.LogInformation("[WI_GEN] Completed work item generation for SpecDocument {SpecDocumentId}: {ItemCount} items produced",
                specDocumentId, items.Count);

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WI_GEN] Failed to generate work items for SpecDocument {SpecDocumentId}", specDocumentId);
            return new List<AdoWorkItemDto>();
        }
    }

    private List<AdoWorkItemDto> ParseWorkItems(string json, int specDocumentId)
    {
        try
        {
            // Strip any accidental markdown fences
            var trimmed = json.Trim();
            if (trimmed.StartsWith("```"))
            {
                var start = trimmed.IndexOf('\n');
                var end = trimmed.LastIndexOf("```");
                if (start >= 0 && end > start)
                    trimmed = trimmed[(start + 1)..end].Trim();
            }

            var items = JsonSerializer.Deserialize<List<AdoWorkItemDto>>(trimmed, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return items ?? new List<AdoWorkItemDto>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[WI_GEN] JSON parse failed for SpecDocument {SpecDocumentId} — returning empty list", specDocumentId);
            return new List<AdoWorkItemDto>();
        }
    }
}
