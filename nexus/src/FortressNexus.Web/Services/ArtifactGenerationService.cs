using System.Text.Json;
using System.Text.RegularExpressions;
using FortressNexus.Web.Data;
using FortressNexus.Web.Models.DTOs;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;
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

    /// <summary>
    /// Thin wrapper kept for interface/test compatibility. Runs the full pipeline and returns the final DTO list.
    /// For production use, prefer <see cref="DecomposeAndPersistAsync"/> which persists incrementally.
    /// </summary>
    public async Task<List<AdoWorkItemDto>> GenerateWorkItemsAsync(int specDocumentId)
    {
        _logger.LogInformation("[WI_GEN] GenerateWorkItemsAsync called for SpecDocument {SpecDocumentId} (thin wrapper)", specDocumentId);

        var specDoc = await _db.SpecDocuments.FirstOrDefaultAsync(s => s.Id == specDocumentId);
        if (specDoc is null)
        {
            _logger.LogWarning("[WI_GEN] SpecDocument {SpecDocumentId} not found", specDocumentId);
            return new List<AdoWorkItemDto>();
        }

        var resolvedModelId = _config["FortressAI:ModelId"] ?? "us.anthropic.claude-sonnet-4-6";
        var specContent = specDoc.EditedContent ?? specDoc.Content;

        try
        {
            return await RunPipelineAsync(specDocumentId, specContent, resolvedModelId,
                artifactSetId: null, batchRecordIds: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WI_GEN] Failed to generate work items for SpecDocument {SpecDocumentId}", specDocumentId);
            return new List<AdoWorkItemDto>();
        }
    }

    public async Task<ArtifactSet> DecomposeAndPersistAsync(int submissionId, int specDocumentId, string callerUpn, string adoProjectName)
    {
        var specDoc = await _db.SpecDocuments.FirstOrDefaultAsync(s => s.Id == specDocumentId)
            ?? throw new InvalidOperationException($"SpecDocument {specDocumentId} not found");

        var specContent = specDoc.EditedContent ?? specDoc.Content;
        var resolvedModelId = _config["FortressAI:ModelId"] ?? "us.anthropic.claude-sonnet-4-6";

        // Load submission for status updates
        var submission = await _db.Submissions.FindAsync(submissionId)
            ?? throw new InvalidOperationException($"Submission {submissionId} not found");

        // Create ArtifactSet (Status=InProgress) and persist immediately
        var artifactSet = new ArtifactSet
        {
            SpecDocumentId = specDocumentId,
            AdoOrganization = "FortressAffinityGroup",
            AdoProjectName = adoProjectName,
            ProcessTemplateTypeId = "6b724908-ef14-45cf-84f8-768b5384da45",
            ExternalDependencyCount = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = callerUpn,
            Status = ArtifactSetStatus.InProgress
        };
        _db.ArtifactSets.Add(artifactSet);
        await _db.SaveChangesAsync(); // get artifactSet.Id

        // Flip submission → Decomposing immediately (before any Bedrock calls)
        submission.Status = SubmissionStatus.Decomposing;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[DECOMP_PERSIST] Submission {SubmissionId}: ArtifactSet {ArtifactSetId} created. Status → Decomposing.",
            submissionId, artifactSet.Id);

        // --- Call 1A (skeleton) ---
        var skeletonSystemPrompt = _config["Nexus:Prompts:ArtifactGenSkeletonSystem"]
            ?? "You are a technical project manager. Output a JSON array of work items. Each item has exactly three fields: type (Epic/Feature/User Story/Task), parentTitle (null for Epics, exact parent title otherwise), and title. No other fields. No descriptions. No AC. Cover every functional area in the spec.";

        List<AdoWorkItemDto> skeletonItems;
        try
        {
            var (call1AText, pt1A, ct1A) = await _bedrock.InvokeAsync(skeletonSystemPrompt, specContent, 64000, resolvedModelId);
            skeletonItems = ParseWorkItems(call1AText, specDocumentId);
            _logger.LogInformation("[DECOMP_PERSIST] Call 1A (skeleton) done for SpecDocument {SpecDocumentId}: {Pt1A}+{Ct1A} tokens, {Count} skeleton items",
                specDocumentId, pt1A, ct1A, skeletonItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DECOMP_PERSIST] Call 1A failed for SpecDocument {SpecDocumentId} — rolling back to Approved", specDocumentId);
            submission.Status = SubmissionStatus.Approved;
            artifactSet.Status = ArtifactSetStatus.Failed;
            artifactSet.ErrorDetail = ex.Message;
            await _db.SaveChangesAsync();
            throw;
        }

        if (skeletonItems.Count == 0)
        {
            submission.Status = SubmissionStatus.Approved;
            artifactSet.Status = ArtifactSetStatus.Failed;
            artifactSet.ErrorDetail = "Call 1A returned 0 work items";
            await _db.SaveChangesAsync();
            throw new InvalidOperationException("Bedrock returned 0 skeleton work items — decomposition failed. Check CloudWatch logs.");
        }

        // Persist skeleton as stub WorkItemRecords (IsEnriched=false)
        var skeletonRecords = skeletonItems.Select(dto => new WorkItemRecord
        {
            ArtifactSetId = artifactSet.Id,
            WorkItemType = dto.WorkItemType,
            Title = dto.Title,
            ParentTitle = dto.ParentTitle,
            PredecessorTitles = dto.PredecessorTitles,
            Status = "Pending",
            IsEnriched = false
        }).ToList();
        _db.WorkItemRecords.AddRange(skeletonRecords);
        await _db.SaveChangesAsync();

        // Build a lookup from skeleton index → WorkItemRecord.Id for fast 1B matching
        var skeletonRecordIds = skeletonRecords.Select(r => r.Id).ToList();

        // Flip ArtifactSet → Enriching (skeleton persisted, enrichment about to start)
        artifactSet.Status = ArtifactSetStatus.Enriching;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[DECOMP_PERSIST] Skeleton persisted ({Count} stubs). ArtifactSet → Enriching.", skeletonRecords.Count);

        // --- Call 1B (enrichment batches) ---
        var enrichSystemPrompt = _config["Nexus:Prompts:ArtifactGenEnrichSystem"]
            ?? "You are a technical project manager. Enrich the provided skeleton JSON array with descriptions, acceptanceCriteria, developerBrief, and activity fields. Do not add or remove items. Do not change titles.";

        const int EnrichBatchSize = 15;
        var totalPt1B = 0;
        var totalCt1B = 0;
        var batchCount = (int)Math.Ceiling((double)skeletonItems.Count / EnrichBatchSize);
        var anyBatchSucceeded = false;

        for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            var batchStartIdx = batchIndex * EnrichBatchSize;
            var batch = skeletonItems.Skip(batchStartIdx).Take(EnrichBatchSize).ToList();
            // IDs for this batch — used to zip response back without a DB round-trip
            var batchIds = skeletonRecordIds.Skip(batchStartIdx).Take(EnrichBatchSize).ToList();

            var batchJson = JsonSerializer.Serialize(batch);
            var call1BUserMessage = $"SKELETON BATCH {batchIndex + 1} of {batchCount}:\n{batchJson}\n\nORIGINAL SPEC:\n{specContent}";

            try
            {
                var (call1BText, pt1B, ct1B) = await _bedrock.InvokeAsync(enrichSystemPrompt, call1BUserMessage, 64000, resolvedModelId);
                totalPt1B += pt1B;
                totalCt1B += ct1B;

                var batchEnriched = ParseWorkItems(call1BText, specDocumentId);

                // Update matching WorkItemRecords in-place (by index, fallback title)
                for (var i = 0; i < batchEnriched.Count; i++)
                {
                    int recordId;
                    if (i < batchIds.Count)
                        recordId = batchIds[i];
                    else
                    {
                        // Fallback: match by title
                        var matchTitle = batchEnriched[i].Title;
                        var fallback = skeletonRecords.FirstOrDefault(r =>
                            string.Equals(r.Title, matchTitle, StringComparison.OrdinalIgnoreCase)
                            && !r.IsEnriched);
                        if (fallback is null) continue;
                        recordId = fallback.Id;
                    }

                    var record = await _db.WorkItemRecords.FindAsync(recordId);
                    if (record is null) continue;

                    var enriched = batchEnriched[i];
                    record.Description = enriched.Description;
                    record.AcceptanceCriteria = enriched.AcceptanceCriteria;
                    // WiTemplate + IsExternalDependency classified after all 1B — set placeholder
                    record.WiTemplate = WiTemplateType.Standard;
                    record.IsExternalDependency = false;
                    record.ExternalOwner = enriched.ExternalOwner;
                    record.IsEnriched = true;
                }

                await _db.SaveChangesAsync();
                anyBatchSucceeded = true;

                _logger.LogInformation("[DECOMP_PERSIST] Call 1B batch {Batch}/{Total} done for SpecDocument {SpecDocumentId}: {Pt1B}+{Ct1B} tokens, {Count} items enriched",
                    batchIndex + 1, batchCount, specDocumentId, pt1B, ct1B, batchEnriched.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DECOMP_PERSIST] Call 1B batch {Batch}/{Total} failed for SpecDocument {SpecDocumentId} — continuing with remaining batches",
                    batchIndex + 1, batchCount, specDocumentId);
                // Partial failure is acceptable — stubs remain with IsEnriched=false
            }
        }

        _logger.LogInformation("[DECOMP_PERSIST] Call 1B complete for SpecDocument {SpecDocumentId}: {Pt1B}+{Ct1B} total tokens, {Batches} batches",
            specDocumentId, totalPt1B, totalCt1B, batchCount);

        // If every batch failed and we have zero enriched items, treat as partial failure
        if (!anyBatchSucceeded && batchCount > 0)
        {
            artifactSet.Status = ArtifactSetStatus.PartialFailure;
            // Submission still advances so user can see stubs
            submission.Status = SubmissionStatus.ArtifactsCreated;
            await _db.SaveChangesAsync();

            _logger.LogWarning("[DECOMP_PERSIST] All 1B batches failed for Submission {SubmissionId} — skeleton-only artifact set created.", submissionId);
            return artifactSet;
        }

        // Reload all records for this artifact set (for sanitize + classify pass)
        var allRecords = await _db.WorkItemRecords
            .Where(r => r.ArtifactSetId == artifactSet.Id)
            .ToListAsync();

        // Build DTO list from persisted records for post-processing
        var enrichedDtos = allRecords.Select(r => new AdoWorkItemDto
        {
            WorkItemType = r.WorkItemType,
            Title = r.Title,
            Description = r.Description,
            AcceptanceCriteria = r.AcceptanceCriteria,
            ParentTitle = r.ParentTitle,
            PredecessorTitles = r.PredecessorTitles,
            IsExternalDependency = r.IsExternalDependency,
            ExternalOwner = r.ExternalOwner,
            WiTemplate = r.WiTemplate,
        }).ToList();

        // Sanitize person names across all records
        SanitizePersonNames(enrichedDtos);

        // Classify WiTemplate + IsExternalDependency and apply back
        for (var i = 0; i < allRecords.Count; i++)
        {
            var dto = enrichedDtos[i];
            dto.WiTemplate = _wiClassifier.ClassifyStory(dto);
            dto.IsExternalDependency = _wiClassifier.IsExternalDependency(dto);
            dto.ExternalOwner = _wiClassifier.ExtractExternalOwner(dto);

            allRecords[i].Title = dto.Title;
            allRecords[i].Description = dto.Description;
            allRecords[i].AcceptanceCriteria = dto.AcceptanceCriteria;
            allRecords[i].WiTemplate = dto.WiTemplate;
            allRecords[i].IsExternalDependency = dto.IsExternalDependency;
            allRecords[i].ExternalOwner = dto.ExternalOwner;
        }
        await _db.SaveChangesAsync();

        // --- Call 2 (TC scan) ---
        var tcCount = 0;
        var tcScanPrompt = _config["Nexus:Prompts:TcScanSystem"];
        if (!string.IsNullOrEmpty(tcScanPrompt))
        {
            var enrichedForTcScan = enrichedDtos.Where(d =>
                !string.IsNullOrWhiteSpace(d.AcceptanceCriteria) ||
                !string.IsNullOrWhiteSpace(d.Description)).ToList();
            var call2UserMessage = $"WORK ITEM ARRAY:\n{JsonSerializer.Serialize(enrichedForTcScan)}\n\nORIGINAL SPEC:\n{specContent}";
            try
            {
                var (call2Text, pt2, ct2) = await _bedrock.InvokeAsync(tcScanPrompt, call2UserMessage, 64000, resolvedModelId);
                _logger.LogInformation("[DECOMP_PERSIST] Call 2 (TC scan) done for SpecDocument {SpecDocumentId}: {Pt2}+{Ct2} tokens",
                    specDocumentId, pt2, ct2);

                var tcResult = ParseTcScanResult(call2Text);
                SanitizePersonNames(tcResult.TestCases);
                tcCount = tcResult.TestCases.Count;

                // Persist TC records (IsEnriched=true — TCs are generated fully enriched)
                var tcRecords = tcResult.TestCases.Select(dto => new WorkItemRecord
                {
                    ArtifactSetId = artifactSet.Id,
                    WorkItemType = dto.WorkItemType,
                    Title = dto.Title,
                    Description = dto.Description,
                    ParentTitle = dto.ParentTitle,
                    WiTemplate = WiTemplateType.TestCase,
                    IsExternalDependency = dto.IsExternalDependency,
                    Status = "Pending",
                    IsEnriched = true
                }).ToList();
                _db.WorkItemRecords.AddRange(tcRecords);

                // Apply TestedBy updates back to parent story records
                var titleToRecord = allRecords
                    .GroupBy(r => r.Title ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
                foreach (var update in tcResult.ParentUpdates)
                {
                    if (titleToRecord.TryGetValue(update.StoryTitle ?? "", out var parent))
                        parent.TestedByTitles = update.TestedByTitles;
                }

                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DECOMP_PERSIST] TC scan failed for SpecDocument {SpecDocumentId} — continuing without TCs", specDocumentId);
            }
        }

        // Update ArtifactSet final state
        var externalDependencyCount = await _db.WorkItemRecords
            .CountAsync(r => r.ArtifactSetId == artifactSet.Id && r.IsExternalDependency);
        artifactSet.ExternalDependencyCount = externalDependencyCount;
        artifactSet.Status = ArtifactSetStatus.Success;
        submission.Status = SubmissionStatus.ArtifactsCreated;
        await _db.SaveChangesAsync();

        var totalCount = await _db.WorkItemRecords.CountAsync(r => r.ArtifactSetId == artifactSet.Id);
        _logger.LogInformation("[DECOMP_PERSIST] Submission {SubmissionId}: ArtifactSet {ArtifactSetId} complete with {Count} WorkItemRecords ({TcCount} TCs). Status → ArtifactsCreated.",
            submissionId, artifactSet.Id, totalCount, tcCount);

        return artifactSet;
    }

    /// <summary>
    /// Internal pipeline used by the thin GenerateWorkItemsAsync wrapper.
    /// Runs 1A + 1B + classification + TC scan and returns a flat DTO list without any DB persistence.
    /// </summary>
    private async Task<List<AdoWorkItemDto>> RunPipelineAsync(
        int specDocumentId,
        string specContent,
        string resolvedModelId,
        int? artifactSetId,
        List<int>? batchRecordIds)
    {
        // Call 1A
        var skeletonSystemPrompt = _config["Nexus:Prompts:ArtifactGenSkeletonSystem"]
            ?? "You are a technical project manager. Output a JSON array of work items. Each item has exactly three fields: type (Epic/Feature/User Story/Task), parentTitle (null for Epics, exact parent title otherwise), and title. No other fields. No descriptions. No AC. Cover every functional area in the spec.";

        var (call1AText, pt1A, ct1A) = await _bedrock.InvokeAsync(skeletonSystemPrompt, specContent, 64000, resolvedModelId);
        var skeletonItems = ParseWorkItems(call1AText, specDocumentId);
        _logger.LogInformation("[WI_GEN] Call 1A (skeleton) done for SpecDocument {SpecDocumentId}: {Pt1A}+{Ct1A} tokens, {Count} items",
            specDocumentId, pt1A, ct1A, skeletonItems.Count);

        // Call 1B
        var enrichSystemPrompt = _config["Nexus:Prompts:ArtifactGenEnrichSystem"]
            ?? "You are a technical project manager. Enrich the provided skeleton JSON array with descriptions, acceptanceCriteria, developerBrief, and activity fields. Do not add or remove items. Do not change titles.";

        const int EnrichBatchSize = 15;
        var enrichedItems = new List<AdoWorkItemDto>();
        var totalPt1B = 0;
        var totalCt1B = 0;
        var batchCount = (int)Math.Ceiling((double)skeletonItems.Count / EnrichBatchSize);

        for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            var batch = skeletonItems.Skip(batchIndex * EnrichBatchSize).Take(EnrichBatchSize).ToList();
            var batchJson = JsonSerializer.Serialize(batch);
            var call1BUserMessage = $"SKELETON BATCH {batchIndex + 1} of {batchCount}:\n{batchJson}\n\nORIGINAL SPEC:\n{specContent}";
            var (call1BText, pt1B, ct1B) = await _bedrock.InvokeAsync(enrichSystemPrompt, call1BUserMessage, 64000, resolvedModelId);
            totalPt1B += pt1B;
            totalCt1B += ct1B;
            var batchEnriched = ParseWorkItems(call1BText, specDocumentId);
            enrichedItems.AddRange(batchEnriched);
            _logger.LogInformation("[WI_GEN] Call 1B batch {Batch}/{Total} done for SpecDocument {SpecDocumentId}: {Pt1B}+{Ct1B} tokens, {Count} items",
                batchIndex + 1, batchCount, specDocumentId, pt1B, ct1B, batchEnriched.Count);
        }

        _logger.LogInformation("[WI_GEN] Call 1B complete for SpecDocument {SpecDocumentId}: {Pt1B}+{Ct1B} total, {Count} items across {Batches} batches",
            specDocumentId, totalPt1B, totalCt1B, enrichedItems.Count, batchCount);

        var items = enrichedItems;
        SanitizePersonNames(items);

        foreach (var item in items)
        {
            item.WiTemplate = _wiClassifier.ClassifyStory(item);
            item.IsExternalDependency = _wiClassifier.IsExternalDependency(item);
            item.ExternalOwner = _wiClassifier.ExtractExternalOwner(item);
        }

        // Call 2 — TC scan
        var tcCount = 0;
        var tcScanPrompt = _config["Nexus:Prompts:TcScanSystem"];
        if (!string.IsNullOrEmpty(tcScanPrompt))
        {
            var enrichedForTcScan = items.Where(d =>
                !string.IsNullOrWhiteSpace(d.AcceptanceCriteria) ||
                !string.IsNullOrWhiteSpace(d.Description)).ToList();
            var call2UserMessage = $"WORK ITEM ARRAY:\n{JsonSerializer.Serialize(enrichedForTcScan)}\n\nORIGINAL SPEC:\n{specContent}";
            try
            {
                var (call2Text, pt2, ct2) = await _bedrock.InvokeAsync(tcScanPrompt, call2UserMessage, 64000, resolvedModelId);
                _logger.LogInformation("[WI_GEN] Call 2 (TC scan) done for SpecDocument {SpecDocumentId}: {Pt2}+{Ct2} tokens",
                    specDocumentId, pt2, ct2);

                var tcResult = ParseTcScanResult(call2Text);
                SanitizePersonNames(tcResult.TestCases);
                items.AddRange(tcResult.TestCases);
                tcCount = tcResult.TestCases.Count;

                var titleMap = items
                    .GroupBy(w => w.Title ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
                foreach (var update in tcResult.ParentUpdates)
                {
                    if (titleMap.TryGetValue(update.StoryTitle ?? "", out var parent))
                        parent.TestedByTitles = update.TestedByTitles;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WI_GEN] TC scan failed for SpecDocument {SpecDocumentId} — returning result without TCs", specDocumentId);
            }
        }

        _logger.LogInformation("[WI_GEN] Completed for SpecDocument {SpecDocumentId}: {ItemCount} items ({TcCount} TCs)",
            specDocumentId, items.Count, tcCount);

        return items;
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

    /// <summary>
    /// Replaces known person names in WI titles and descriptions with role-based equivalents.
    /// Handles "As [Name], I want..." user story patterns and inline mentions.
    /// </summary>
    private static void SanitizePersonNames(List<AdoWorkItemDto> items)
    {
        // Name → role replacement map
        var nameRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rob Nethery"]     = "the Network/Infrastructure team",
            ["Rob"]             = "the Network/Infrastructure team",
            ["Tony"]            = "the developer",
            ["Clint"]           = "the reviewer",
            ["Fred"]            = "the administrator",
            ["Elise"]           = "the administrator",
            ["Elise Lippe"]     = "the administrator",
        };

        foreach (var item in items)
        {
            item.Title       = ReplaceNames(item.Title, nameRoles);
            item.Description = ReplaceNames(item.Description, nameRoles);
            item.AcceptanceCriteria = ReplaceNames(item.AcceptanceCriteria, nameRoles);
        }
    }

    private static string ReplaceNames(string? text, Dictionary<string, string> nameRoles)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        // Longest names first to avoid partial replacement ("Rob Nethery" before "Rob")
        foreach (var (name, role) in nameRoles.OrderByDescending(kv => kv.Key.Length))
        {
            // "As Rob Nethery, I want" → "As the Network/Infrastructure team representative, I want"
            text = Regex.Replace(text,
                $@"(?<=\bAs\s+){Regex.Escape(name)}(?=\s*,)",
                $"{role} representative",
                RegexOptions.IgnoreCase);

            // "Send ... to Rob" / "request to Rob" inline mentions — word-boundary match
            text = Regex.Replace(text,
                $@"\b{Regex.Escape(name)}\b",
                role,
                RegexOptions.IgnoreCase);
        }

        return text;
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
