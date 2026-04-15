using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FortressNexus.Web.Data;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;
using FortressNexus.Web.Services;

namespace FortressNexus.Web.Services.Discovery;

public class DiscoveryService : IDiscoveryService
{
    private readonly IDbContextFactory<NexusDbContext> _dbFactory;
    private readonly IKnowledgeBaseService _kb;
    private readonly BedrockService _bedrock;
    private readonly IConfiguration _config;
    private readonly DiscoveryInferenceConfig _inferenceConfig;
    private readonly IFileStorageService _fileStorage;
    private readonly SpecGenInferenceConfig _specGenConfig;
    private readonly ILogger<DiscoveryService> _logger;

    // DTOs for JSON deserialization of Bedrock response
    private record QuestionDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("blocking")] bool Blocking,
        [property: JsonPropertyName("rationale")] string? Rationale);

    private record QuestionsResponse(
        [property: JsonPropertyName("questions")] List<QuestionDto> Questions);

    public DiscoveryService(
        IDbContextFactory<NexusDbContext> dbFactory,
        IKnowledgeBaseService kb,
        BedrockService bedrock,
        IConfiguration config,
        IOptions<DiscoveryInferenceConfig> inferenceConfig,
        IOptions<SpecGenInferenceConfig> specGenConfig,
        IFileStorageService fileStorage,
        ILogger<DiscoveryService> logger)
    {
        _dbFactory = dbFactory;
        _kb = kb;
        _bedrock = bedrock;
        _config = config;
        _inferenceConfig = inferenceConfig.Value;
        _specGenConfig = specGenConfig.Value;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Guid> InitiateDiscoveryAsync(int submissionId, CancellationToken ct = default)
    {
        var session = new DiscoverySession
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            Status = DiscoverySessionStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.DiscoverySessions.Add(session);

        var submission = await db.Submissions.FindAsync(new object[] { submissionId }, ct)
            ?? throw new KeyNotFoundException($"Submission {submissionId} not found");
        submission.DiscoveryStatus = DiscoverySessionStatus.Pending;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[DISCOVERY] Session {SessionId} created for submission {SubmissionId}",
            session.Id, submissionId);

        // Fire question generation in background — fire-and-forget with its own scope
        _ = Task.Run(async () =>
        {
            try { await GenerateQuestionsAsync(session.Id, submissionId, CancellationToken.None); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DISCOVERY] Background question generation failed for session {SessionId}",
                    session.Id);
            }
        }, CancellationToken.None);

        return session.Id;
    }

    public async Task<DiscoverySession?> GetSessionAsync(int submissionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.DiscoverySessions
            .Where(s => s.SubmissionId == submissionId
                   && s.Status != DiscoverySessionStatus.Superseded)
            .OrderByDescending(s => s.CreatedAt)
            .Include(s => s.Questions)
                .ThenInclude(q => q.Answer)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<DiscoverySession>> GetAllSessionsAsync(int submissionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.DiscoverySessions
            .Where(s => s.SubmissionId == submissionId)
            .OrderByDescending(s => s.CreatedAt)
            .Include(s => s.Questions)
                .ThenInclude(q => q.Answer)
            .ToListAsync(ct);
    }

    public async Task SaveAnswersAsync(Guid sessionId,
        IEnumerable<(Guid QuestionId, string? Answer)> answers,
        string answeredByOid, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var answerList = answers.ToList();
        _logger.LogInformation("[DISCOVERY] SaveAnswersAsync: {Count} answers for session {SessionId}", answerList.Count, sessionId);

        foreach (var (questionId, answerText) in answerList)
        {
            // Check for existing answer
            var existing = await db.DiscoveryAnswers
                .FirstOrDefaultAsync(a => a.DiscoveryQuestionId == questionId, ct);

            if (existing != null)
            {
                // Update existing
                existing.AnswerText = answerText;
                existing.AnsweredBy = answeredByOid;
                existing.AnsweredAt = now;
            }
            else
            {
                // Insert new
                db.DiscoveryAnswers.Add(new DiscoveryAnswer
                {
                    Id = Guid.NewGuid(),
                    DiscoveryQuestionId = questionId,
                    AnswerText = answerText,
                    AnsweredBy = answeredByOid,
                    AnsweredAt = now
                });
            }
        }

        var session = await db.DiscoverySessions
            .Include(s => s.Submission)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session != null)
        {
            session.Status = DiscoverySessionStatus.Answered;
            session.SkippedByUser = false;
            session.AnsweredAt = now;
            session.UpdatedAt = now;
            if (session.Submission != null)
                session.Submission.DiscoveryStatus = DiscoverySessionStatus.Answered;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("[DISCOVERY] Answers saved for session {SessionId} by {User}",
            sessionId, answeredByOid);
    }

    public async Task SkipDiscoveryAsync(Guid sessionId, string skippedByOid, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = await db.DiscoverySessions
            .Include(s => s.Submission)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null) return;

        session.Status = DiscoverySessionStatus.Skipped;
        session.SkippedByUser = true;
        session.UpdatedAt = DateTime.UtcNow;
        if (session.Submission != null)
            session.Submission.DiscoveryStatus = DiscoverySessionStatus.Skipped;

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("[DISCOVERY] Session {SessionId} skipped by {User}", sessionId, skippedByOid);
    }

    public async Task SupersedeSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = await db.DiscoverySessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null) return;

        session.Status = DiscoverySessionStatus.Superseded;
        session.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("[DISCOVERY] Session {SessionId} superseded for submission {SubmissionId}",
            sessionId, session.SubmissionId);
    }

    public async Task<string> BuildSpecContextAsync(int submissionId, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(submissionId, ct);
        if (session == null || session.SkippedByUser || !session.Questions.Any())
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("DISCOVERY CONVERSATION RESULTS:");
        sb.AppendLine("The following questions were asked before spec generation. Answers are authoritative.");
        sb.AppendLine();

        foreach (var q in session.Questions.OrderBy(q => q.SortOrder))
        {
            var required = q.IsBlocking ? "REQUIRED" : "OPTIONAL";
            sb.AppendLine($"Q{q.SortOrder + 1} [{q.Category} — {required}]: {q.QuestionText}");
            sb.AppendLine($"A{q.SortOrder + 1}: {q.Answer?.AnswerText ?? "[Not answered — user skipped]"}");
            sb.AppendLine();
        }

        sb.AppendLine("Where a question was skipped, call it out as an open question in Section 9 (Out of Scope / Deferred).");
        return sb.ToString();
    }

    private async Task GenerateQuestionsAsync(Guid sessionId, int submissionId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // 1. Load submission (title + narrative + file names)
        var submission = await db.Submissions
            .Include(s => s.SubmissionFiles)
                .ThenInclude(sf => sf.UploadedFile)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct);

        if (submission == null)
        {
            _logger.LogWarning("[DISCOVERY_GEN] Submission {SubmissionId} not found — aborting question gen", submissionId);
            return;
        }

        var session = await db.DiscoverySessions.FindAsync(new object[] { sessionId }, ct);
        if (session == null)
        {
            _logger.LogWarning("[DISCOVERY_GEN] Session {SessionId} not found — aborting question gen", sessionId);
            return;
        }

        // 2. Build KB query
        var narrativeTruncated = submission.NarrativeText.Length > 1500
            ? submission.NarrativeText[..1500]
            : submission.NarrativeText;
        var kbQuery = $"{submission.Title}. {narrativeTruncated}";

        // 3. Retrieve KB passages
        var passages = (await _kb.RetrieveAsync(kbQuery, 5, ct)).ToList();

        // 4. Assemble prompt
        var systemPrompt = _config["Nexus:Prompts:DiscoverySystem"]
            ?? "You are a business analyst generating discovery questions for a software feature request. Output JSON only.";

        var questionGenPrompt = _config["Nexus:Prompts:DiscoveryQuestionGen"]
            ?? "Generate 3-7 discovery questions to clarify the feature request. Return JSON with a 'questions' array where each item has: id (string), text (string), category (string), blocking (bool), rationale (string).";

        var userPromptSb = new StringBuilder();

        if (passages.Any())
        {
            userPromptSb.AppendLine("## Relevant Knowledge Base Context");
            foreach (var (passage, idx) in passages.Select((p, i) => (p, i + 1)))
            {
                userPromptSb.AppendLine($"### Passage {idx} (score: {passage.Score:F3}, source: {passage.SourceUri})");
                userPromptSb.AppendLine(passage.Content);
                userPromptSb.AppendLine();
            }
        }

        userPromptSb.AppendLine("## Feature Request");
        userPromptSb.AppendLine($"**Title:** {submission.Title}");
        userPromptSb.AppendLine($"**Feature Area:** {submission.FeatureArea ?? "Not specified"}");
        userPromptSb.AppendLine();
        userPromptSb.AppendLine("## BA Narrative");
        userPromptSb.AppendLine(submission.NarrativeText);

        var files = submission.SubmissionFiles
            .Select(sf => sf.UploadedFile)
            .Where(f => f != null)
            .ToList();

        // Pre-process large text files in parallel — summarize if > 40K chars
        var textFileIds = files
            .Where(f => f != null && (f.FileType == FileType.Html || f.FileType == FileType.Pdf ||
                                       f.FileType == FileType.Text || f.FileType == FileType.Other)
                                   && !string.IsNullOrWhiteSpace(f.ProcessedText))
            .Select(f => f!.Id)
            .ToHashSet();

        var summarizeTasks = files
            .Where(f => f != null && textFileIds.Contains(f!.Id) && f.ProcessedText!.Length > 40_000)
            .Select(async f =>
            {
                var summaryPrompt = $"Summarize the following document for use as context in software feature discovery. Preserve key requirements, constraints, and technical details.\n\n{f!.ProcessedText}";
                try
                {
                    using var sumCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    sumCts.CancelAfter(TimeSpan.FromSeconds(120));
                    var result = await _bedrock.InvokeAsync(
                        "You are a technical document summarizer.",
                        summaryPrompt,
                        maxTokens: 10_000,
                        modelId: _inferenceConfig.ModelId,
                        sumCts.Token);
                    return (FileId: f.Id, Summary: result.Text);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "[DISCOVERY_GEN] Summarization pre-pass failed for file {FileId} — using truncated verbatim", f.Id);
                    return (FileId: f.Id, Summary: (string?)null);
                }
            });

        var summaries = (await Task.WhenAll(summarizeTasks))
            .ToDictionary(r => r.FileId, r => r.Summary);

        if (files.Any())
        {
            userPromptSb.AppendLine();
            userPromptSb.AppendLine("## Attached Files");

            int imageCount = 0;
            foreach (var file in files)
            {
                userPromptSb.AppendLine($"### {file!.OriginalFileName} ({file.FileType})");

                switch (file.FileType)
                {
                    case FileType.Html:
                    case FileType.Pdf:
                    case FileType.Text:
                    case FileType.Other:
                        if (!string.IsNullOrWhiteSpace(file.ProcessedText))
                        {
                            string content;
                            if (file.ProcessedText.Length > 40_000)
                            {
                                // Use summarization result, or fall back to first 40K if summarization failed
                                content = summaries.TryGetValue(file.Id, out var summary) && summary != null
                                    ? $"[Summarized — original {file.ProcessedText.Length:N0} chars]\n{summary}"
                                    : file.ProcessedText[..40_000] + "\n... [truncated — summarization failed]";
                            }
                            else
                            {
                                content = file.ProcessedText; // verbatim, no truncation
                            }
                            userPromptSb.AppendLine("**Contents:**");
                            userPromptSb.AppendLine(content);
                        }
                        else
                        {
                            userPromptSb.AppendLine("*[File content not available]*");
                        }
                        break;

                    case FileType.Image:
                        if (imageCount >= 5)
                        {
                            userPromptSb.AppendLine("*[Additional image — skipped (limit 5)]*");
                            break;
                        }
                        imageCount++;

                        try
                        {
                            using var imageStream = await _fileStorage.DownloadAsync(file.S3Key, file.S3Bucket);
                            using var ms = new MemoryStream();
                            await imageStream.CopyToAsync(ms, ct);
                            var imageBytes = ms.ToArray();

                            string? imageDescription = null;
                            const int maxVisionAttempts = 2;

                            for (int attempt = 1; attempt <= maxVisionAttempts; attempt++)
                            {
                                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                attemptCts.CancelAfter(TimeSpan.FromSeconds(_specGenConfig.TimeoutSeconds));

                                try
                                {
                                    string visionUserPrompt;
                                    if (!string.IsNullOrWhiteSpace(file.UserDescription))
                                        visionUserPrompt = $"Describe this image for discovery question generation. Context provided by submitter: {file.UserDescription}";
                                    else
                                        visionUserPrompt = $"Describe this image in the context of the feature: {submission.Title}";

                                    var visionResult = await _bedrock.InvokeWithImageAsync(
                                        "You are a business analyst assistant. Describe the contents of this image concisely for the purpose of generating discovery questions about a software feature.",
                                        visionUserPrompt,
                                        imageBytes,
                                        file.ContentType,
                                        2000,
                                        _specGenConfig.VisionModelId,
                                        attemptCts.Token);

                                    imageDescription = visionResult.Text;
                                    break;
                                }
                                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                                {
                                    _logger.LogWarning("[DISCOVERY_GEN] Vision timeout on attempt {Attempt}/{Max} for file {FileId}", attempt, maxVisionAttempts, file.Id);
                                    if (attempt < maxVisionAttempts)
                                        await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
                                }
                            }

                            if (imageDescription != null)
                            {
                                userPromptSb.AppendLine($"## Image: {file.OriginalFileName}");
                                userPromptSb.AppendLine(imageDescription);
                            }
                            else
                            {
                                _logger.LogWarning("[DISCOVERY_GEN] Vision failed all attempts for image {FileId} — skipping", file.Id);
                                userPromptSb.AppendLine($"*[Image: {file.OriginalFileName} — vision timed out]*");
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                        {
                            _logger.LogWarning(ex, "[DISCOVERY_GEN] Vision call failed for image file {FileId}", file.Id);
                            userPromptSb.AppendLine($"*[Image: {file.OriginalFileName} — vision failed]*");
                        }
                        break;

                    default:
                        userPromptSb.AppendLine("*[Binary or unsupported file type]*");
                        break;
                }

                userPromptSb.AppendLine();
            }
        }

        userPromptSb.AppendLine();
        userPromptSb.AppendLine(questionGenPrompt);

        string userPrompt = userPromptSb.ToString();

        _logger.LogInformation("[DISCOVERY_GEN] Full prompt ({Chars} chars):\n{Prompt}", userPrompt.Length, userPrompt);

        // 5. Call Bedrock
        string rawResponse;
        try
        {
            var result = await _bedrock.InvokeAsync(
                systemPrompt,
                userPrompt,
                maxTokens: _inferenceConfig.MaxTokens,
                modelId: _inferenceConfig.ModelId);
            rawResponse = result.Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DISCOVERY_GEN] Bedrock call failed for session {SessionId}", sessionId);
            session.Status = DiscoverySessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            submission.DiscoveryStatus = DiscoverySessionStatus.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        // 6. Parse JSON response (strip fences)
        var jsonText = rawResponse.Trim();
        if (jsonText.StartsWith("```"))
        {
            var lines = jsonText.Split('\n').ToList();
            // Remove first line (```json or ```) and last line (```)
            if (lines.Count > 2)
            {
                lines = lines.Skip(1).ToList();
                if (lines.Last().TrimEnd() == "```")
                    lines = lines.Take(lines.Count - 1).ToList();
            }
            jsonText = string.Join('\n', lines);
        }

        QuestionsResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<QuestionsResponse>(jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException jex)
        {
            _logger.LogWarning(jex, "[DISCOVERY_GEN] JSON parse failed for session {SessionId} — raw: {Raw}",
                sessionId, rawResponse[..Math.Min(500, rawResponse.Length)]);
        }

        // 7 / 8. Save questions or mark Failed
        if (parsed?.Questions is { Count: > 0 })
        {
            for (int i = 0; i < parsed.Questions.Count; i++)
            {
                var dto = parsed.Questions[i];
                db.DiscoveryQuestions.Add(new DiscoveryQuestion
                {
                    Id = Guid.NewGuid(),
                    DiscoverySessionId = sessionId,
                    SortOrder = i,
                    QuestionText = dto.Text,
                    Category = dto.Category,
                    IsBlocking = dto.Blocking,
                    Rationale = dto.Rationale,
                    CreatedAt = DateTime.UtcNow
                });
            }

            session.Status = DiscoverySessionStatus.QuestionsReady;
            session.QuestionCount = parsed.Questions.Count;
            session.KbQueryUsed = kbQuery;
            session.KbPassagesRetrieved = passages.Count;
            session.GeneratedAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            submission.DiscoveryStatus = DiscoverySessionStatus.QuestionsReady;

            _logger.LogInformation(
                "[DISCOVERY_GEN] Session {SessionId}: {QuestionCount} questions generated, {KbCount} KB passages used",
                sessionId, parsed.Questions.Count, passages.Count);
        }
        else
        {
            session.Status = DiscoverySessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            submission.DiscoveryStatus = DiscoverySessionStatus.Failed;

            _logger.LogWarning("[DISCOVERY_GEN] Session {SessionId}: parse produced no questions — status=Failed",
                sessionId);
        }

        await db.SaveChangesAsync(ct);
    }
}
