using Microsoft.EntityFrameworkCore;
using FortressNexus.Web.Data;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;

namespace FortressNexus.Web.Services;

public class SpecGenerationService : ISpecGenerationService
{
    private readonly NexusDbContext _db;
    private readonly BedrockService _bedrock;
    private readonly IFileStorageService _fileStorage;
    private readonly IConfiguration _config;
    private readonly ILogger<SpecGenerationService> _logger;

    public SpecGenerationService(
        NexusDbContext db,
        BedrockService bedrock,
        IFileStorageService fileStorage,
        IConfiguration config,
        ILogger<SpecGenerationService> logger)
    {
        _db = db;
        _bedrock = bedrock;
        _fileStorage = fileStorage;
        _config = config;
        _logger = logger;
    }

    public async Task<SpecDocument> GenerateAsync(int submissionId)
    {
        // 1. Load submission with file
        var submission = await _db.Submissions
            .Include(s => s.MockupFile)
            .FirstOrDefaultAsync(s => s.Id == submissionId)
            ?? throw new KeyNotFoundException($"Submission {submissionId} not found.");

        var file = submission.MockupFile
            ?? throw new KeyNotFoundException($"Submission {submissionId} has no associated mockup file.");

        // 2. System prompt from config
        var systemPrompt = _config["Nexus:Prompts:SpecGenSystem"]
            ?? "You are a business analyst generating software specification documents. Produce clear, detailed, structured specs.";

        // 3. Build user prompt
        var userPrompt = $"""
            ## Feature Request

            **Title:** {submission.Title}
            **Feature Area:** {submission.FeatureArea ?? "Not specified"}

            ## BA Narrative
            {submission.NarrativeText}

            ## UI Mockup Content
            {file.ProcessedText ?? "[No mockup text available]"}

            ---

            Generate a complete spec document following the standard template.
            """;

        // 4. Call AI — use vision if image, otherwise text
        (string Text, int PromptTokens, int CompletionTokens) result;

        bool isImage = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        if (isImage)
        {
            _logger.LogInformation("[SPEC_GEN] Submission {Id} has image mockup ({ContentType}) — attempting vision call",
                submissionId, file.ContentType);
            try
            {
                var stream = await _fileStorage.DownloadAsync(file.S3Key);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var imageBytes = ms.ToArray();
                result = await _bedrock.InvokeWithImageAsync(systemPrompt, userPrompt, imageBytes, file.ContentType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SPEC_GEN] Vision call failed for submission {Id} — falling back to text-only", submissionId);
                result = await _bedrock.InvokeAsync(systemPrompt, userPrompt);
            }
        }
        else
        {
            result = await _bedrock.InvokeAsync(systemPrompt, userPrompt);
        }

        // 5. Create SpecDocument
        var specDoc = new SpecDocument
        {
            SubmissionId = submissionId,
            Version = 1,
            Content = result.Text,
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = "ai",
            PromptTokensUsed = result.PromptTokens,
            CompletionTokensUsed = result.CompletionTokens
        };

        // 6. Save SpecDocument
        _db.SpecDocuments.Add(specDoc);
        await _db.SaveChangesAsync();

        // 7 & 8. Update Submission
        submission.ActiveSpecDocumentId = specDoc.Id;
        submission.Status = SubmissionStatus.AwaitingReview;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[SPEC_GEN] Generated SpecDocument {SpecId} v{Version} for Submission {SubId}",
            specDoc.Id, specDoc.Version, submissionId);

        return specDoc;
    }

    public async Task<SpecDocument> RegenerateAsync(int specDocumentId)
    {
        // 1. Load existing SpecDocument
        var existing = await _db.SpecDocuments
            .FirstOrDefaultAsync(s => s.Id == specDocumentId)
            ?? throw new KeyNotFoundException($"SpecDocument {specDocumentId} not found.");

        int submissionId = existing.SubmissionId;

        // 2. Get next version number
        int nextVersion = await _db.SpecDocuments
            .Where(s => s.SubmissionId == submissionId)
            .MaxAsync(s => (int?)s.Version) ?? 0;
        nextVersion += 1;

        // 3. Generate new spec (calls same AI logic)
        var newSpec = await GenerateAsync(submissionId);

        // Update version to correct number (GenerateAsync always sets Version=1)
        newSpec.Version = nextVersion;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[SPEC_GEN] Regenerated SpecDocument {SpecId} v{Version} for Submission {SubId}",
            newSpec.Id, newSpec.Version, submissionId);

        return newSpec;
    }
}
