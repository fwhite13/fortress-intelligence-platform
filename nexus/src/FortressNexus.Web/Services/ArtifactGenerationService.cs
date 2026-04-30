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
            // Call 1 — Decomposition (TC-stripped prompt, 32768 max tokens)
            var (call1Text, pt1, ct1) = await _bedrock.InvokeAsync(systemPrompt, specContent, 32768, resolvedModelId);

            _logger.LogInformation("[WI_GEN] Call 1 (decomposition) completed for SpecDocument {SpecDocumentId}: {Pt1} + {Ct1} tokens",
                specDocumentId, pt1, ct1);

            var items = ParseWorkItems(call1Text, specDocumentId);

            // Classify each WI candidate
            foreach (var item in items)
            {
                item.WiTemplate = _wiClassifier.ClassifyStory(item);
                item.IsExternalDependency = _wiClassifier.IsExternalDependency(item);
                item.ExternalOwner = _wiClassifier.ExtractExternalOwner(item);
            }

            // Call 2 — TC Compliance Scan
            var tcCount = 0;
            var tcScanPrompt = _config["Nexus:Prompts:TcScanSystem"];
            if (!string.IsNullOrEmpty(tcScanPrompt))
            {
                var call2UserMessage = $"WORK ITEM ARRAY:\n{JsonSerializer.Serialize(items)}\n\nORIGINAL SPEC:\n{specContent}";
                try
                {
                    var (call2Text, pt2, ct2) = await _bedrock.InvokeAsync(tcScanPrompt, call2UserMessage, 32768, resolvedModelId);
                    _logger.LogInformation("[WI_GEN] Call 2 (TC scan) completed for SpecDocument {SpecDocumentId}: {Pt2} + {Ct2} tokens",
                        specDocumentId, pt2, ct2);

                    var tcResult = ParseTcScanResult(call2Text);
                    items.AddRange(tcResult.TestCases);
                    tcCount = tcResult.TestCases.Count;

                    var titleMap = items.ToDictionary(w => w.Title ?? "", w => w);
                    foreach (var update in tcResult.ParentUpdates)
                    {
                        if (titleMap.TryGetValue(update.StoryTitle ?? "", out var parent))
                            parent.TestedByTitles = update.TestedByTitles;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[WI_GEN] TC scan failed for SpecDocument {SpecDocumentId} — returning decomposition result without TCs",
                        specDocumentId);
                }
            }

            _logger.LogInformation("[WI_GEN] Completed work item generation for SpecDocument {SpecDocumentId}: {ItemCount} items produced ({TestCaseCount} test cases)",
                specDocumentId, items.Count, tcCount);

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

    private TcScanResult ParseTcScanResult(string json)
    {
        var trimmed = json.Trim();
        if (trimmed.StartsWith("```"))
        {
            var start = trimmed.IndexOf('\n');
            var end = trimmed.LastIndexOf("```");
            if (start >= 0 && end > start)
                trimmed = trimmed[(start + 1)..end].Trim();
        }

        using var doc = JsonDocument.Parse(trimmed);
        var root = doc.RootElement;

        var testCases = new List<AdoWorkItemDto>();
        if (root.TryGetProperty("testCases", out var tcArray))
        {
            foreach (var tc in tcArray.EnumerateArray())
            {
                var dto = new AdoWorkItemDto
                {
                    WorkItemType = "Test Case",
                    WiTemplate = WiTemplateType.TestCase,
                    Title = tc.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    ParentTitle = tc.TryGetProperty("parentTitle", out var pt) ? pt.GetString() : null,
                    IsExternalDependency = tc.TryGetProperty("isExternalDependency", out var ied) && ied.GetBoolean(),
                };

                if (tc.TryGetProperty("rationale", out var rat))
                    dto.Description = rat.GetString();

                if (tc.TryGetProperty("tags", out var tags))
                    dto.Tags = tags.EnumerateArray().Select(x => x.GetString() ?? "").ToList();

                testCases.Add(dto);
            }
        }

        var parentUpdates = new List<TcParentUpdate>();
        if (root.TryGetProperty("parentUpdates", out var puArray))
        {
            foreach (var pu in puArray.EnumerateArray())
            {
                var storyTitle = pu.TryGetProperty("storyTitle", out var st) ? st.GetString() : null;
                List<string>? testedBy = null;
                if (pu.TryGetProperty("testedByTitles", out var tbt))
                    testedBy = tbt.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                parentUpdates.Add(new TcParentUpdate(storyTitle, testedBy));
            }
        }

        return new TcScanResult(testCases, parentUpdates);
    }

    private record TcScanResult(List<AdoWorkItemDto> TestCases, List<TcParentUpdate> ParentUpdates);
    private record TcParentUpdate(string? StoryTitle, List<string>? TestedByTitles);

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
