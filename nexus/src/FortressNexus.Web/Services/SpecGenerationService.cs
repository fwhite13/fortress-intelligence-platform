using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using FortressNexus.Web.Data;
using FortressNexus.Web.Models;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;

namespace FortressNexus.Web.Services;

public class SpecGenerationService : ISpecGenerationService
{
    private readonly NexusDbContext _db;
    private readonly BedrockService _bedrock;
    private readonly IFileStorageService _fileStorage;
    private readonly IMockupSectionizer _sectionizer;
    private readonly IConfiguration _config;
    private readonly ILogger<SpecGenerationService> _logger;
    private readonly SpecGenInferenceConfig _specGenConfig;
    private readonly FortressNexus.Web.Services.Discovery.IDiscoveryService? _discoveryService;

    public SpecGenerationService(
        NexusDbContext db,
        BedrockService bedrock,
        IFileStorageService fileStorage,
        IMockupSectionizer sectionizer,
        IConfiguration config,
        ILogger<SpecGenerationService> logger,
        IOptions<SpecGenInferenceConfig> specGenOptions,
        FortressNexus.Web.Services.Discovery.IDiscoveryService? discoveryService = null)
    {
        _db = db;
        _bedrock = bedrock;
        _fileStorage = fileStorage;
        _sectionizer = sectionizer;
        _config = config;
        _logger = logger;
        _specGenConfig = specGenOptions.Value;
        _discoveryService = discoveryService;
    }

    public async Task<SpecDocument> GenerateAsync(int submissionId)
    {
        // 1. Load submission with SubmissionFiles -> UploadedFile
        var submission = await _db.Submissions
            .Include(s => s.SubmissionFiles)
                .ThenInclude(sf => sf.UploadedFile)
            .FirstOrDefaultAsync(s => s.Id == submissionId)
            ?? throw new KeyNotFoundException($"Submission {submissionId} not found.");

        // 2. Transition: Pending -> Generating
        submission.Status = SubmissionStatus.Generating;
        await _db.SaveChangesAsync();

        // Overall 10-minute timeout for generation (vision retry worst case: 5 files × 120s × 3 attempts = ~18min, but typical is much less)
        using var overallCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            // 3. System prompt
            var systemPrompt = _config["Nexus:Prompts:SpecGenSystem"]
                ?? "You are a business analyst generating software specification documents. Produce clear, detailed, structured specs.";

            // 4. Build multi-file prompt
            var userPrompt = await BuildPromptAsync(submission, systemPrompt, overallCts.Token);

            // 4b. Inject discovery context if available
            if (_discoveryService != null)
            {
                try
                {
                    var discoveryContext = await _discoveryService.BuildSpecContextAsync(submissionId, overallCts.Token);
                    if (!string.IsNullOrEmpty(discoveryContext))
                        userPrompt = userPrompt + "\n\n" + discoveryContext;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SPEC_GEN] Discovery context load failed — continuing without it");
                }
            }

            // 5. Call AI
            var result = await _bedrock.InvokeAsync(systemPrompt, userPrompt, _specGenConfig.MaxTokens, _specGenConfig.ModelId, overallCts.Token);

            // 6. Create SpecDocument — compute next version (MAX+1, starts at 1 if none exist)
            var nextVersion = await _db.SpecDocuments
                .Where(s => s.SubmissionId == submissionId)
                .Select(s => (int?)s.Version)
                .MaxAsync() ?? 0;
            nextVersion += 1;

            var specDoc = new SpecDocument
            {
                SubmissionId = submissionId,
                Version = nextVersion,
                Content = result.Text,
                GeneratedAt = DateTime.UtcNow,
                GeneratedBy = "ai",
                PromptTokensUsed = result.PromptTokens,
                CompletionTokensUsed = result.CompletionTokens
            };

            _db.SpecDocuments.Add(specDoc);
            await _db.SaveChangesAsync();

            // 7. Update Submission -> AwaitingReview
            submission.ActiveSpecDocumentId = specDoc.Id;
            submission.Status = SubmissionStatus.AwaitingReview;
            await _db.SaveChangesAsync();

            _logger.LogInformation("[SPEC_GEN] Generated SpecDocument {SpecDocumentId} v{Version} for Submission {SubmissionId} — PromptTokens={PromptTokens} CompletionTokens={CompletionTokens}",
                specDoc.Id, specDoc.Version, submissionId, result.PromptTokens, result.CompletionTokens);

            return specDoc;
        }
        catch (OperationCanceledException) when (overallCts.IsCancellationRequested)
        {
            _logger.LogError("[SPEC_GEN] Overall generation timeout (10min) for submission {SubId} — setting Failed", submissionId);
            submission.Status = SubmissionStatus.Failed;
            await _db.SaveChangesAsync();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SPEC_GEN] Failed to generate spec for submission {SubId}", submissionId);
            submission.Status = SubmissionStatus.Failed;
            await _db.SaveChangesAsync();
            throw;
        }
    }

    private async Task<string> BuildPromptAsync(Submission submission, string systemPrompt, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Feature Request");
        sb.AppendLine();
        sb.AppendLine($"**Title:** {submission.Title}");
        sb.AppendLine($"**Feature Area:** {submission.FeatureArea ?? "Not specified"}");
        sb.AppendLine();
        sb.AppendLine("## BA Narrative");
        sb.AppendLine(submission.NarrativeText);

        var files = submission.SubmissionFiles
            .OrderBy(sf => sf.SortOrder)
            .Select(sf => sf.UploadedFile)
            .Where(f => f is not null)
            .ToList();

        if (files.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine("*No mockup files attached — narrative-only submission.*");
        }
        else
        {
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[i]!;
                sb.AppendLine();
                sb.AppendLine($"## File {i + 1}: {file.OriginalFileName} ({file.FileType})");

                switch (file.FileType)
                {
                    case FileType.Html:
                        sb.AppendLine("**File Type: HTML**");
                        if (!string.IsNullOrWhiteSpace(file.ProcessedText))
                        {
                            // Sectionize for richer structure
                            var sections = await _sectionizer.SectionizeAsync(
                                file.ProcessedText, submission.Id.ToString());
                            foreach (var section in sections)
                            {
                                sb.AppendLine($"### Section: {section.Label}");
                                sb.AppendLine(section.TextContent);
                                sb.AppendLine();
                            }
                        }
                        else
                        {
                            sb.AppendLine("*HTML file — no text content extracted.*");
                        }
                        break;

                    case FileType.Image:
                        sb.AppendLine("**File Type: Image**");
                        // Vision call per image — 120s per-call timeout, up to 3 attempts with backoff
                        try
                        {
                            var imageStream = await _fileStorage.DownloadAsync(file.S3Key, file.S3Bucket);
                            using var ms = new MemoryStream();
                            await imageStream.CopyToAsync(ms);
                            var imageBytes = ms.ToArray();

                            (string Text, int PromptTokens, int CompletionTokens) visionResult = default;
                            bool visionSucceeded = false;
                            const int maxAttempts = 3;

                            for (int attempt = 1; attempt <= maxAttempts; attempt++)
                            {
                                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                attemptCts.CancelAfter(TimeSpan.FromSeconds(_specGenConfig.TimeoutSeconds));

                                try
                                {
                                    visionResult = await _bedrock.InvokeWithImageAsync(
                                        systemPrompt,
                                        $"Describe what you see in this UI mockup image for the feature: {submission.Title}",
                                        imageBytes,
                                        file.ContentType,
                                        _specGenConfig.VisionMaxTokens,
                                        _specGenConfig.VisionModelId,
                                        attemptCts.Token);

                                    visionSucceeded = true;
                                    break;
                                }
                                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                                {
                                    // Per-attempt timeout (not overall CTS cancel)
                                    _logger.LogWarning("[SPEC_GEN] Vision call timed out (attempt {Attempt}/{Max}) for file {FileId}", attempt, maxAttempts, file.Id);
                                    if (attempt < maxAttempts)
                                        await Task.Delay(TimeSpan.FromSeconds(3 * attempt), cancellationToken); // backoff: 3s, 6s
                                }
                                // OperationCanceledException with cancellationToken.IsCancellationRequested → rethrows, exits loop, caught by outer catch
                            }

                            if (visionSucceeded)
                            {
                                sb.AppendLine($"**Vision Analysis:**");
                                sb.AppendLine(visionResult.Text);
                            }
                            else
                            {
                                _logger.LogWarning("[SPEC_GEN] Vision call failed all {Max} attempts for file {FileId} — skipping", maxAttempts, file.Id);
                                sb.AppendLine("*Image vision analysis timed out — skipped.*");
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogWarning(ex, "[SPEC_GEN] Vision call failed for file {S3Key}", file.S3Key);
                            sb.AppendLine("*Image vision analysis failed — skipped.*");
                        }
                        break;

                    case FileType.Text:
                        sb.AppendLine("**File Type: Text**");
                        if (!string.IsNullOrWhiteSpace(file.ProcessedText))
                        {
                            sb.AppendLine($"**File Contents: {file.OriginalFileName}**");
                            sb.AppendLine(file.ProcessedText);
                        }
                        else
                        {
                            sb.AppendLine("*Text file — no content available.*");
                        }
                        break;

                    case FileType.Pdf:
                        sb.AppendLine("**File Type: PDF**");
                        if (!string.IsNullOrWhiteSpace(file.ProcessedText))
                            sb.AppendLine(file.ProcessedText);
                        else
                            sb.AppendLine("*PDF file — no text content available (extraction may have failed at upload time).*");
                        break;

                    case FileType.Other:
                    default:
                        sb.AppendLine("**File Type: Unknown/Unsupported**");
                        sb.AppendLine("*[Binary or unsupported file type — content not included]*");
                        break;
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("Generate a complete spec document following the standard template.");

        return sb.ToString();
    }

    public async Task<SpecDocument> RegenerateAsync(int specDocumentId)
    {
        var existing = await _db.SpecDocuments
            .FirstOrDefaultAsync(s => s.Id == specDocumentId)
            ?? throw new KeyNotFoundException($"SpecDocument {specDocumentId} not found.");

        int submissionId = existing.SubmissionId;

        int nextVersion = await _db.SpecDocuments
            .Where(s => s.SubmissionId == submissionId)
            .MaxAsync(s => (int?)s.Version) ?? 0;
        nextVersion += 1;

        var newSpec = await GenerateAsync(submissionId);
        newSpec.Version = nextVersion;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[SPEC_GEN] Regenerated SpecDocument {SpecId} v{Version} for Submission {SubId}",
            newSpec.Id, newSpec.Version, submissionId);

        return newSpec;
    }
}
