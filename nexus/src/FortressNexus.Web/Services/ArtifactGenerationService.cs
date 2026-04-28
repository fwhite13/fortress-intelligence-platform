using System.Text.Json;
using System.Text.RegularExpressions;
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
    private readonly IWiClassifier _wiClassifier;

    private static readonly Regex AcCheckboxPattern = new(
        @"^\s*-\s*\[.\]\s*(.+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex AcNumberedPattern = new(
        @"^\s*\d+\.\s+(.+)", RegexOptions.Multiline | RegexOptions.Compiled);

    public ArtifactGenerationService(
        NexusDbContext db,
        BedrockService bedrock,
        IConfiguration config,
        ILogger<ArtifactGenerationService> logger,
        IWiClassifier wiClassifier)
    {
        _db = db;
        _bedrock = bedrock;
        _config = config;
        _logger = logger;
        _wiClassifier = wiClassifier;
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

            // Classify each WI candidate
            foreach (var item in items)
            {
                item.WiTemplate = _wiClassifier.ClassifyStory(item);
                item.IsExternalDependency = _wiClassifier.IsExternalDependency(item);
                item.ExternalOwner = _wiClassifier.ExtractExternalOwner(item);
            }

            // Generate Test Case WIs for qualifying User Stories
            var testCases = new List<AdoWorkItemDto>();
            foreach (var story in items.Where(w => w.WorkItemType == "User Story"))
            {
                if (_wiClassifier.ShouldGenerateTestCases(story))
                {
                    var acItems = ParseAcItems(story.AcceptanceCriteria);
                    var tcTitles = new List<string>();
                    foreach (var acItem in acItems)
                    {
                        var tc = new AdoWorkItemDto
                        {
                            WorkItemType = "Test Case",
                            WiTemplate = WiTemplateType.TestCase,
                            Title = $"TC: {acItem.Trim()}",
                            ParentTitle = story.Title,
                            Description = $"Test case for acceptance criterion: {acItem.Trim()}",
                        };
                        testCases.Add(tc);
                        tcTitles.Add(tc.Title);
                    }
                    story.TestedByTitles = tcTitles;
                }
            }
            items.AddRange(testCases);

            _logger.LogInformation("[WI_GEN] Completed work item generation for SpecDocument {SpecDocumentId}: {ItemCount} items produced ({TestCaseCount} test cases)",
                specDocumentId, items.Count, testCases.Count);

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

    internal static List<string> ParseAcItems(string? acceptanceCriteria)
    {
        if (string.IsNullOrWhiteSpace(acceptanceCriteria))
            return new List<string>();

        // Try checkbox pattern first: - [ ] or - [x]
        var checkboxMatches = AcCheckboxPattern.Matches(acceptanceCriteria);
        if (checkboxMatches.Count > 0)
            return checkboxMatches.Select(m => m.Groups[1].Value.Trim()).Where(s => s.Length > 0).ToList();

        // Try numbered list pattern: 1. item
        var numberedMatches = AcNumberedPattern.Matches(acceptanceCriteria);
        if (numberedMatches.Count > 0)
            return numberedMatches.Select(m => m.Groups[1].Value.Trim()).Where(s => s.Length > 0).ToList();

        // Fallback: split on newlines, filter non-empty
        return acceptanceCriteria
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }
}
