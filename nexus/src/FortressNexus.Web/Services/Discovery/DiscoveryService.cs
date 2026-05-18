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

    // ── DTOs for JSON deserialization of Bedrock response ──────────────────
    private record QuestionDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("blocking")] bool Blocking,
        [property: JsonPropertyName("rationale")] string? Rationale);

    private record QuestionsResponse(
        [property: JsonPropertyName("questions")] List<QuestionDto> Questions,
        [property: JsonPropertyName("readyToAdvance")] bool ReadyToAdvance,
        [property: JsonPropertyName("advanceRationale")] string? AdvanceRationale);

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

    // ── Public: initiation ─────────────────────────────────────────────────

    public async Task<Guid> InitiateDiscoveryAsync(int submissionId, CancellationToken ct = default)
    {
        var session = new DiscoverySession
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            Status = DiscoverySessionStatus.Phase1Active,
            Phase = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.DiscoverySessions.Add(session);

        var submission = await db.Submissions.FindAsync(new object[] { submissionId }, ct)
            ?? throw new KeyNotFoundException($"Submission {submissionId} not found");
        submission.DiscoveryStatus = DiscoverySessionStatus.Phase1Active;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[DISCOVERY] Session {SessionId} created for submission {SubmissionId} (Phase1Active)",
            session.Id, submissionId);

        // Fire Phase 1 Round 1 question generation in background
        _ = Task.Run(async () =>
        {
            try { await GeneratePhase1QuestionsAsync(session.Id, submissionId, round: 1, CancellationToken.None); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DISCOVERY] Background Phase1 question generation failed for session {SessionId}", session.Id);
            }
        }, CancellationToken.None);

        return session.Id;
    }

    // ── Public: query ──────────────────────────────────────────────────────

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

    // ── Public: legacy single-round save (backward compat) ─────────────────

    public async Task SaveAnswersAsync(Guid sessionId,
        IEnumerable<(Guid QuestionId, string? Answer)> answers,
        string answeredByOid, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var answerList = answers.ToList();
        _logger.LogInformation("[DISCOVERY] SaveAnswersAsync (legacy): {Count} answers for session {SessionId}", answerList.Count, sessionId);

        foreach (var (questionId, answerText) in answerList)
        {
            var existing = await db.DiscoveryAnswers
                .FirstOrDefaultAsync(a => a.DiscoveryQuestionId == questionId, ct);

            if (existing != null)
            {
                existing.AnswerText = answerText;
                existing.AnsweredBy = answeredByOid;
                existing.AnsweredAt = now;
            }
            else
            {
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
        _logger.LogInformation("[DISCOVERY] Legacy answers saved for session {SessionId} by {User}", sessionId, answeredByOid);
    }

    // ── Public: two-phase iterative answer save ────────────────────────────

    /// <summary>
    /// Saves answers for the given phase/round, fires next-round generation in background,
    /// and completes the phase if readyToAdvance was already set or this is round 3.
    /// </summary>
    public async Task SaveRoundAnswersAsync(Guid sessionId, int phase, int round,
        IEnumerable<(Guid QuestionId, string? Answer)> answers,
        string upn, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var answerList = answers.ToList();
        _logger.LogInformation("[DISCOVERY] SaveRoundAnswersAsync: phase={Phase} round={Round} answers={Count} session={SessionId}",
            phase, round, answerList.Count, sessionId);

        // 1. Persist answers
        foreach (var (questionId, answerText) in answerList)
        {
            var existing = await db.DiscoveryAnswers
                .FirstOrDefaultAsync(a => a.DiscoveryQuestionId == questionId, ct);

            if (existing != null)
            {
                existing.AnswerText = answerText;
                existing.AnsweredBy = upn;
                existing.AnsweredAt = now;
            }
            else
            {
                db.DiscoveryAnswers.Add(new DiscoveryAnswer
                {
                    Id = Guid.NewGuid(),
                    DiscoveryQuestionId = questionId,
                    AnswerText = answerText,
                    AnsweredBy = upn,
                    AnsweredAt = now
                });
            }
        }

        var session = await db.DiscoverySessions
            .Include(s => s.Submission)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        // Check IsFinalRound flag: if set, complete the phase now (answers just submitted).
        // Otherwise generate the next round (unless round >= 3 as a safety fallback).
        bool isFinalRound = session.IsFinalRound || round >= 3;

        session.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        // 2. In background: either complete the phase OR generate the next round
        var sessionIdCapture = sessionId;
        var submissionIdCapture = session.SubmissionId;
        var roundCapture = round;

        _ = Task.Run(async () =>
        {
            try
            {
                if (isFinalRound)
                {
                    // Complete this phase now that the user has answered the final round
                    _logger.LogInformation("[DISCOVERY] Phase {Phase} completing after final-round answer submission for session {SessionId}",
                        phase, sessionIdCapture);
                    await CompletePhaseInternalAsync(sessionIdCapture, phase, terminatedByUser: false, CancellationToken.None);
                }
                else if (phase == 1)
                {
                    // Generate Phase 1 follow-up round
                    await GeneratePhase1QuestionsAsync(sessionIdCapture, submissionIdCapture, roundCapture + 1, CancellationToken.None);
                }
                else
                {
                    // Generate Phase 2 follow-up round
                    await GeneratePhase2QuestionsAsync(sessionIdCapture, submissionIdCapture, roundCapture + 1, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DISCOVERY] Background next-round generation failed for session {SessionId} phase={Phase} round={Round}",
                    sessionIdCapture, phase, roundCapture + 1);
            }
        }, CancellationToken.None);
    }

    // ── Public: phase advance ──────────────────────────────────────────────

    /// <summary>
    /// Admin-only gate: transitions Phase1Complete → Phase2Active and fires Phase 2 Round 1 generation.
    /// </summary>
    public async Task AdvanceToPhase2Async(Guid sessionId, string upn, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var session = await db.DiscoverySessions
            .Include(s => s.Submission)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            _logger.LogWarning("[DISCOVERY] AdvanceToPhase2: session {SessionId} not found", sessionId);
            return;
        }

        if (session.Status != DiscoverySessionStatus.Phase1Complete)
        {
            _logger.LogWarning("[DISCOVERY] AdvanceToPhase2: session {SessionId} is not Phase1Complete (status={Status}) — ignoring",
                sessionId, session.Status);
            return;
        }

        session.Status = DiscoverySessionStatus.Phase2Active;
        session.Phase = 2;
        session.UpdatedAt = DateTime.UtcNow;
        if (session.Submission != null)
            session.Submission.DiscoveryStatus = DiscoverySessionStatus.Phase2Active;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[DISCOVERY] Session {SessionId} advanced to Phase2Active by {User}", sessionId, upn);

        var submissionId = session.SubmissionId;

        // Fire Phase 2 Round 1 question generation in background
        _ = Task.Run(async () =>
        {
            try { await GeneratePhase2QuestionsAsync(sessionId, submissionId, round: 1, CancellationToken.None); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DISCOVERY] Background Phase2 question generation failed for session {SessionId}", sessionId);
            }
        }, CancellationToken.None);
    }

    // ── Public: skip / supersede ───────────────────────────────────────────

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

    // ── Public: spec context builder (ND-5) ───────────────────────────────

    /// <summary>
    /// Assembles the discovery Q&amp;A transcript across all phases and rounds for injection
    /// into the spec generation prompt.
    /// </summary>
    public async Task<string> BuildSpecContextAsync(int submissionId, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(submissionId, ct);
        if (session == null) return string.Empty;

        // If both phases were fully skipped, return empty
        if (session.SkippedByUser && session.Phase == 1 && !session.Questions.Any())
            return string.Empty;

        var questions = session.Questions
            .OrderBy(q => q.Phase)
            .ThenBy(q => q.Round)
            .ThenBy(q => q.SortOrder)
            .ToList();

        if (!questions.Any()) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("DISCOVERY CONVERSATION RESULTS:");
        sb.AppendLine("The following questions were asked before spec generation. Answers are authoritative.");
        sb.AppendLine();

        // Group by phase
        var byPhase = questions.GroupBy(q => q.Phase).OrderBy(g => g.Key);

        foreach (var phaseGroup in byPhase)
        {
            var phaseName = phaseGroup.Key == 1 ? "PHASE 1: BUSINESS DISCOVERY" : "PHASE 2: TECHNICAL DISCOVERY";
            sb.AppendLine($"--- {phaseName} ---");
            sb.AppendLine();

            var byRound = phaseGroup.GroupBy(q => q.Round).OrderBy(g => g.Key);

            foreach (var roundGroup in byRound)
            {
                var roundLabel = roundGroup.Key == 1
                    ? "Round 1:"
                    : $"Round {roundGroup.Key} (follow-up):";
                sb.AppendLine(roundLabel);

                int qIdx = 1;
                foreach (var q in roundGroup.OrderBy(q => q.SortOrder))
                {
                    var required = q.IsBlocking ? "REQUIRED" : "OPTIONAL";
                    sb.AppendLine($"Q{qIdx} [{q.Category} — {required}]: {q.QuestionText}");
                    sb.AppendLine($"A{qIdx}: {q.Answer?.AnswerText ?? "[Not answered — user skipped]"}");
                    qIdx++;
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("Where a question was skipped, call it out as an open question in Section 9 (Out of Scope / Deferred).");
        return sb.ToString();
    }

    // ── Private: phase completion ──────────────────────────────────────────

    private async Task CompletePhaseInternalAsync(Guid sessionId, int phase, bool terminatedByUser,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = await db.DiscoverySessions
            .Include(s => s.Submission)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null) return;

        var now = DateTime.UtcNow;

        if (phase == 1)
        {
            session.Phase1CompletedAt = now;
            session.Phase1TerminatedByUser = terminatedByUser;
            session.Status = DiscoverySessionStatus.Phase1Complete;
            if (session.Submission != null)
                session.Submission.DiscoveryStatus = DiscoverySessionStatus.Phase1Complete;
        }
        else
        {
            session.Phase2CompletedAt = now;
            session.Phase2TerminatedByUser = terminatedByUser;
            session.Status = DiscoverySessionStatus.Phase2Complete;
            if (session.Submission != null)
                session.Submission.DiscoveryStatus = DiscoverySessionStatus.Phase2Complete;
        }

        session.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[DISCOVERY] Session {SessionId} Phase {Phase} complete (terminatedByUser={ByUser})",
            sessionId, phase, terminatedByUser);
    }

    // ── Private: Phase 1 question generation (ND-2) ───────────────────────

    private async Task GeneratePhase1QuestionsAsync(Guid sessionId, int submissionId, int round,
        CancellationToken ct)
    {
        _logger.LogInformation("[DISCOVERY_GEN] GeneratePhase1Questions: session={SessionId} round={Round}", sessionId, round);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var submission = await db.Submissions
            .Include(s => s.SubmissionFiles)
                .ThenInclude(sf => sf.UploadedFile)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct);

        if (submission == null)
        {
            _logger.LogWarning("[DISCOVERY_GEN] Submission {SubmissionId} not found — aborting Phase1 gen", submissionId);
            return;
        }

        var session = await db.DiscoverySessions.FindAsync(new object[] { sessionId }, ct);
        if (session == null)
        {
            _logger.LogWarning("[DISCOVERY_GEN] Session {SessionId} not found — aborting Phase1 gen", sessionId);
            return;
        }

        // Build KB query from submission narrative
        var narrativeTruncated = submission.NarrativeText.Length > 1500
            ? submission.NarrativeText[..1500]
            : submission.NarrativeText;
        var kbQuery = $"{submission.Title}. {narrativeTruncated}";

        // KB retrieval (same for all rounds — fresh retrieval each time)
        var passages = (await _kb.RetrieveAsync(kbQuery, 5, ct)).ToList();

        // System prompt from config
        var systemPrompt = _config["Nexus:Prompts:Discovery:Phase1System"]
            ?? _config["Nexus:Prompts:DiscoverySystem"]
            ?? "You are a business analyst generating discovery questions. Output JSON only.";

        // Question gen instruction (round-aware)
        var questionGenInstruction = round == 1
            ? (_config["Nexus:Prompts:Discovery:Phase1QuestionGen"]
               ?? "Generate questions to clarify the business goals, scope, users, and workflows. Return JSON.")
            : (_config["Nexus:Prompts:Discovery:Phase1FollowUpGen"]
               ?? "Review the answers and generate follow-up questions if gaps remain. Return JSON.");

        // Assemble user message
        var userPromptSb = new StringBuilder();
        AppendKbPassages(userPromptSb, passages);
        AppendSubmissionHeader(userPromptSb, submission);
        await AppendFileContentsAsync(userPromptSb, submission, db, ct);

        // For round 2+: include all prior Phase 1 Q&A as context
        if (round > 1)
        {
            await AppendPriorQAAsync(userPromptSb, sessionId, phase: 1, db, ct);
        }

        userPromptSb.AppendLine();
        userPromptSb.AppendLine(questionGenInstruction);

        var userPrompt = userPromptSb.ToString();
        _logger.LogInformation("[DISCOVERY_GEN] Phase1 prompt ({Chars} chars)", userPrompt.Length);

        // Invoke Bedrock
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
            _logger.LogError(ex, "[DISCOVERY_GEN] Bedrock Phase1 call failed for session {SessionId}", sessionId);
            session.Status = DiscoverySessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            if (submission != null) submission.DiscoveryStatus = DiscoverySessionStatus.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Parse response
        var parsed = ParseQuestionsResponse(rawResponse, sessionId);

        if (parsed == null)
        {
            session.Status = DiscoverySessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            submission.DiscoveryStatus = DiscoverySessionStatus.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Force readyToAdvance if this is round 3 (max rounds hit)
        var readyToAdvance = parsed.ReadyToAdvance || round >= 3;

        if (parsed.Questions.Count > 0)
        {
            // Save questions with phase=1, round=N
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
                    Phase = 1,
                    Round = (byte)round,
                    CreatedAt = DateTime.UtcNow
                });
            }

            session.QuestionCount = (session.QuestionCount ?? 0) + parsed.Questions.Count;
            session.KbQueryUsed = kbQuery;
            session.KbPassagesRetrieved = passages.Count;
            session.GeneratedAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;

            if (readyToAdvance)
            {
                // This is the final round — mark IsFinalRound so SaveRoundAnswersAsync
                // completes the phase AFTER the user submits answers (not now).
                session.IsFinalRound = true;
                session.Status = DiscoverySessionStatus.Phase1Active;
                submission.DiscoveryStatus = DiscoverySessionStatus.Phase1Active;
                _logger.LogInformation("[DISCOVERY_GEN] Phase1 round {Round}: final round flagged (IsFinalRound=true) for session {SessionId}",
                    round, sessionId);
            }
            else
            {
                session.Status = DiscoverySessionStatus.Phase1Active;
                submission.DiscoveryStatus = DiscoverySessionStatus.Phase1Active;
                _logger.LogInformation("[DISCOVERY_GEN] Phase1 round {Round}: {Count} questions saved for session {SessionId}",
                    round, parsed.Questions.Count, sessionId);
            }
        }
        else if (readyToAdvance)
        {
            // Agent returned readyToAdvance=true with no new questions —
            // treat as an immediate complete (no answers to wait for).
            session.Phase1CompletedAt = DateTime.UtcNow;
            session.Status = DiscoverySessionStatus.Phase1Complete;
            submission.DiscoveryStatus = DiscoverySessionStatus.Phase1Complete;
            session.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("[DISCOVERY_GEN] Phase1 complete (no new questions) for session {SessionId}", sessionId);
        }
        else
        {
            session.Status = DiscoverySessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            submission.DiscoveryStatus = DiscoverySessionStatus.Failed;
            _logger.LogWarning("[DISCOVERY_GEN] Phase1 round {Round}: no questions returned — status=Failed for session {SessionId}",
                round, sessionId);
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Private: Phase 2 question generation (ND-3) ───────────────────────

    private async Task GeneratePhase2QuestionsAsync(Guid sessionId, int submissionId, int round,
        CancellationToken ct)
    {
        _logger.LogInformation("[DISCOVERY_GEN] GeneratePhase2Questions: session={SessionId} round={Round}", sessionId, round);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var submission = await db.Submissions
            .Include(s => s.SubmissionFiles)
                .ThenInclude(sf => sf.UploadedFile)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct);

        if (submission == null)
        {
            _logger.LogWarning("[DISCOVERY_GEN] Submission {SubmissionId} not found — aborting Phase2 gen", submissionId);
            return;
        }

        var session = await db.DiscoverySessions.FindAsync(new object[] { sessionId }, ct);
        if (session == null)
        {
            _logger.LogWarning("[DISCOVERY_GEN] Session {SessionId} not found — aborting Phase2 gen", sessionId);
            return;
        }

        // Build enriched KB query using Phase 1 answers for richer context
        var phase1Summary = await BuildPhase1SummaryForKbQueryAsync(sessionId, submission, db, ct);
        var kbQuery = phase1Summary.Length > 0 ? phase1Summary : $"{submission.Title}. {submission.NarrativeText[..Math.Min(1500, submission.NarrativeText.Length)]}";

        var passages = (await _kb.RetrieveAsync(kbQuery, 5, ct)).ToList();

        // System prompt from config
        var systemPrompt = _config["Nexus:Prompts:Discovery:Phase2System"]
            ?? _config["Nexus:Prompts:DiscoverySystem"]
            ?? "You are a software architect generating technical discovery questions. Output JSON only.";

        // Question gen instruction (round-aware)
        var questionGenInstruction = round == 1
            ? (_config["Nexus:Prompts:Discovery:Phase2QuestionGen"]
               ?? "Generate technical questions that must be answered before writing a complete specification. Return JSON.")
            : (_config["Nexus:Prompts:Discovery:Phase2FollowUpGen"]
               ?? "Review the technical answers and generate follow-up questions if gaps remain. Return JSON.");

        // Assemble user message — Phase 1 transcript always included first
        var userPromptSb = new StringBuilder();

        // Phase 1 Q&A transcript (always present in Phase 2)
        userPromptSb.AppendLine("## Phase 1 Discovery Results");
        userPromptSb.AppendLine("The following business discovery was completed before these technical questions were generated.");
        userPromptSb.AppendLine();
        await AppendPriorQAAsync(userPromptSb, sessionId, phase: 1, db, ct);
        userPromptSb.AppendLine();

        AppendKbPassages(userPromptSb, passages);
        AppendSubmissionHeader(userPromptSb, submission);
        await AppendFileContentsAsync(userPromptSb, submission, db, ct);

        // For round 2+: include prior Phase 2 Q&A as well
        if (round > 1)
        {
            userPromptSb.AppendLine("## Prior Phase 2 Q&A");
            await AppendPriorQAAsync(userPromptSb, sessionId, phase: 2, db, ct);
        }

        userPromptSb.AppendLine();
        userPromptSb.AppendLine(questionGenInstruction);

        var userPrompt = userPromptSb.ToString();
        _logger.LogInformation("[DISCOVERY_GEN] Phase2 prompt ({Chars} chars)", userPrompt.Length);

        // Invoke Bedrock
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
            _logger.LogError(ex, "[DISCOVERY_GEN] Bedrock Phase2 call failed for session {SessionId}", sessionId);
            session.Status = DiscoverySessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            submission.DiscoveryStatus = DiscoverySessionStatus.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Parse response
        var parsed = ParseQuestionsResponse(rawResponse, sessionId);

        if (parsed == null)
        {
            session.Status = DiscoverySessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            submission.DiscoveryStatus = DiscoverySessionStatus.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        var readyToAdvance = parsed.ReadyToAdvance || round >= 3;

        if (parsed.Questions.Count > 0)
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
                    Phase = 2,
                    Round = (byte)round,
                    CreatedAt = DateTime.UtcNow
                });
            }

            session.QuestionCount = (session.QuestionCount ?? 0) + parsed.Questions.Count;
            session.UpdatedAt = DateTime.UtcNow;

            if (readyToAdvance)
            {
                // Final round — defer Phase2Complete until user submits answers.
                session.IsFinalRound = true;
                session.Status = DiscoverySessionStatus.Phase2Active;
                submission.DiscoveryStatus = DiscoverySessionStatus.Phase2Active;
                _logger.LogInformation("[DISCOVERY_GEN] Phase2 round {Round}: final round flagged (IsFinalRound=true) for session {SessionId}",
                    round, sessionId);
            }
            else
            {
                session.Status = DiscoverySessionStatus.Phase2Active;
                submission.DiscoveryStatus = DiscoverySessionStatus.Phase2Active;
                _logger.LogInformation("[DISCOVERY_GEN] Phase2 round {Round}: {Count} questions saved for session {SessionId}",
                    round, parsed.Questions.Count, sessionId);
            }
        }
        else if (readyToAdvance)
        {
            // No new questions — immediate complete.
            session.Phase2CompletedAt = DateTime.UtcNow;
            session.Status = DiscoverySessionStatus.Phase2Complete;
            submission.DiscoveryStatus = DiscoverySessionStatus.Phase2Complete;
            session.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("[DISCOVERY_GEN] Phase2 complete (no new questions) for session {SessionId}", sessionId);
        }
        else
        {
            session.Status = DiscoverySessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            submission.DiscoveryStatus = DiscoverySessionStatus.Failed;
            _logger.LogWarning("[DISCOVERY_GEN] Phase2 round {Round}: no questions returned — status=Failed for session {SessionId}",
                round, sessionId);
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Private: helpers ───────────────────────────────────────────────────

    private static void AppendKbPassages(StringBuilder sb, List<KbPassage> passages)
    {
        if (!passages.Any()) return;
        sb.AppendLine("## Relevant Knowledge Base Context");
        foreach (var (passage, idx) in passages.Select((p, i) => (p, i + 1)))
        {
            sb.AppendLine($"### Passage {idx} (score: {passage.Score:F3}, source: {passage.SourceUri})");
            sb.AppendLine(passage.Content);
            sb.AppendLine();
        }
    }

    private static void AppendSubmissionHeader(StringBuilder sb, Submission submission)
    {
        sb.AppendLine("## Feature Request");
        sb.AppendLine($"**Title:** {submission.Title}");
        sb.AppendLine($"**Feature Area:** {submission.FeatureArea ?? "Not specified"}");
        sb.AppendLine();
        sb.AppendLine("## BA Narrative");
        sb.AppendLine(submission.NarrativeText);
    }

    private async Task AppendFileContentsAsync(StringBuilder sb, Submission submission,
        NexusDbContext db, CancellationToken ct)
    {
        var files = submission.SubmissionFiles
            .Select(sf => sf.UploadedFile)
            .Where(f => f != null)
            .ToList();

        if (!files.Any()) return;

        // Pre-summarize large text files
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
                    _logger.LogWarning(ex, "[DISCOVERY_GEN] Summarization failed for file {FileId}", f.Id);
                    return (FileId: f.Id, Summary: (string?)null);
                }
            });

        var summaries = (await Task.WhenAll(summarizeTasks))
            .ToDictionary(r => r.FileId, r => r.Summary);

        sb.AppendLine();
        sb.AppendLine("## Attached Files");

        int imageCount = 0;
        foreach (var file in files)
        {
            sb.AppendLine($"### {file!.OriginalFileName} ({file.FileType})");

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
                            content = summaries.TryGetValue(file.Id, out var summary) && summary != null
                                ? $"[Summarized — original {file.ProcessedText.Length:N0} chars]\n{summary}"
                                : file.ProcessedText[..40_000] + "\n... [truncated]";
                        }
                        else
                        {
                            content = file.ProcessedText;
                        }
                        sb.AppendLine("**Contents:**");
                        sb.AppendLine(content);
                    }
                    else
                    {
                        sb.AppendLine("*[File content not available]*");
                    }
                    break;

                case FileType.Image:
                    if (imageCount >= 5)
                    {
                        sb.AppendLine("*[Additional image — skipped (limit 5)]*");
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
                                var visionPrompt = !string.IsNullOrWhiteSpace(file.UserDescription)
                                    ? $"Describe the UI elements, layout, labels, and interactions visible in this image. Submitter note: {file.UserDescription}. Be specific and complete."
                                    : "Describe the UI elements, layout, labels, and interactions visible in this image. Be specific and complete.";

                                var visionResult = await _bedrock.InvokeWithImageAsync(
                                    "You are a business analyst assistant. Describe the contents of this image concisely for discovery question generation.",
                                    visionPrompt,
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
                            sb.AppendLine($"**Image description:** {imageDescription}");
                        }
                        else
                        {
                            sb.AppendLine($"*[Image: {file.OriginalFileName} — vision timed out]*");
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "[DISCOVERY_GEN] Vision call failed for file {FileId}", file.Id);
                        sb.AppendLine($"*[Image: {file.OriginalFileName} — vision failed]*");
                    }
                    break;

                default:
                    sb.AppendLine("*[Binary or unsupported file type]*");
                    break;
            }

            sb.AppendLine();
        }
    }

    private async Task AppendPriorQAAsync(StringBuilder sb, Guid sessionId, int phase,
        NexusDbContext db, CancellationToken ct)
    {
        var priorQuestions = await db.DiscoveryQuestions
            .Where(q => q.DiscoverySessionId == sessionId && q.Phase == phase)
            .Include(q => q.Answer)
            .OrderBy(q => q.Round)
            .ThenBy(q => q.SortOrder)
            .ToListAsync(ct);

        if (!priorQuestions.Any()) return;

        var byRound = priorQuestions.GroupBy(q => q.Round).OrderBy(g => g.Key);
        foreach (var roundGroup in byRound)
        {
            var label = roundGroup.Key == 1 ? "Round 1:" : $"Round {roundGroup.Key} (follow-up):";
            sb.AppendLine(label);
            int idx = 1;
            foreach (var q in roundGroup.OrderBy(q => q.SortOrder))
            {
                var required = q.IsBlocking ? "REQUIRED" : "OPTIONAL";
                sb.AppendLine($"Q{idx} [{q.Category} — {required}]: {q.QuestionText}");
                sb.AppendLine($"A{idx}: {q.Answer?.AnswerText ?? "[Not answered]"}");
                idx++;
            }
            sb.AppendLine();
        }
    }

    private async Task<string> BuildPhase1SummaryForKbQueryAsync(Guid sessionId, Submission submission,
        NexusDbContext db, CancellationToken ct)
    {
        // Build an enriched KB query by appending Phase 1 answer text to the submission narrative
        var phase1Answers = await db.DiscoveryAnswers
            .Where(a => a.Question.DiscoverySessionId == sessionId && a.Question.Phase == 1)
            .Select(a => a.AnswerText)
            .ToListAsync(ct);

        var combined = string.Join(". ", new[] { submission.Title }
            .Concat(phase1Answers.Where(a => !string.IsNullOrWhiteSpace(a))!));
        return combined.Length > 2000 ? combined[..2000] : combined;
    }

    private QuestionsResponse? ParseQuestionsResponse(string rawResponse, Guid sessionId)
    {
        var jsonText = rawResponse.Trim();
        if (jsonText.StartsWith("```"))
        {
            var lines = jsonText.Split('\n').ToList();
            if (lines.Count > 2)
            {
                lines = lines.Skip(1).ToList();
                if (lines.Last().TrimEnd() == "```")
                    lines = lines.Take(lines.Count - 1).ToList();
            }
            jsonText = string.Join('\n', lines);
        }

        try
        {
            return JsonSerializer.Deserialize<QuestionsResponse>(jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException jex)
        {
            _logger.LogWarning(jex, "[DISCOVERY_GEN] JSON parse failed for session {SessionId} — raw: {Raw}",
                sessionId, rawResponse[..Math.Min(500, rawResponse.Length)]);
            return null;
        }
    }
}
