using Amazon.BedrockRuntime;
using FortressAI.V2.Web.Data;
using Microsoft.EntityFrameworkCore;
using BedrockMessage = Amazon.BedrockRuntime.Model.Message;
using BedrockContentBlock = Amazon.BedrockRuntime.Model.ContentBlock;
using ConversationRole = Amazon.BedrockRuntime.ConversationRole;
using ConverseRequest = Amazon.BedrockRuntime.Model.ConverseRequest;
using Message = FortressAI.V2.Web.Data.Models.Message;

namespace FortressAI.V2.Web.Services;

public class CompactionService : ICompactionService
{
    private const int CompactionThreshold = 140_000; // 70% of 200k
    private const int LiveWindowSize = 20;
    private const string ExtractionModel = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly IRAGWriteService _ragWriteService;
    private readonly ILogger<CompactionService> _logger;

    public CompactionService(
        IDbContextFactory<FaitV2DbContext> dbFactory,
        IAmazonBedrockRuntime bedrockRuntime,
        IRAGWriteService ragWriteService,
        ILogger<CompactionService> logger)
    {
        _dbFactory = dbFactory;
        _bedrockRuntime = bedrockRuntime;
        _ragWriteService = ragWriteService;
        _logger = logger;
    }

    public async Task<bool> ShouldCompactAsync(string conversationId, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);
        var tokenCount = await db.Conversations
            .Where(c => c.Id == conversationId)
            .Select(c => c.EstimatedTokenCount)
            .FirstOrDefaultAsync(cts.Token);

        return tokenCount >= CompactionThreshold;
    }

    public async Task CompactIfNeededAsync(string conversationId, CancellationToken ct = default)
    {
        if (!await ShouldCompactAsync(conversationId, ct))
            return;

        _logger.LogInformation("Compaction triggered for conversation {ConvId}", conversationId);

        try
        {
            List<Message> liveMessages;
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);
                liveMessages = await db.Messages
                    .Where(m => m.ConversationId == conversationId
                             && m.CompactedAt == null
                             && !m.IsCompactionSummary)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync(cts.Token);
            }

            if (liveMessages.Count <= LiveWindowSize)
            {
                _logger.LogInformation("Compaction skipped — only {Count} live messages, below window size", liveMessages.Count);
                return;
            }

            var compactionTarget = liveMessages.Take(liveMessages.Count - LiveWindowSize).ToList();
            var extractionText = string.Join("\n\n", compactionTarget.Select(m => $"[{m.Role.ToUpper()}]: {m.Content}"));

            var extractionOutput = await ExtractFactsAsync(extractionText, ct);

            string summaryId;
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);

                var summaryMsg = new Message
                {
                    Id = Guid.NewGuid().ToString(),
                    ConversationId = conversationId,
                    Role = "system",
                    Content = extractionOutput,
                    IsCompactionSummary = true,
                    CreatedAt = compactionTarget[0].CreatedAt,
                    TokenCount = EstimateTokens(extractionOutput),
                    SessionType = "main"
                };
                db.Messages.Add(summaryMsg);
                summaryId = summaryMsg.Id;

                var targetIds = compactionTarget.Select(m => m.Id).ToList();
                await db.Messages
                    .Where(m => targetIds.Contains(m.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.CompactedAt, DateTime.UtcNow), cts.Token);

                await db.SaveChangesAsync(cts.Token);
            }

            await RecalculateTokenCountAsync(conversationId, ct);

            await _ragWriteService.QueueExtractionAsync(
                conversationId,
                $"{compactionTarget.First().Id}..{compactionTarget.Last().Id}",
                ct);

            _logger.LogInformation("Compaction complete for {ConvId}. Compacted {Count} messages into summary {SummaryId}",
                conversationId, compactionTarget.Count, summaryId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Compaction failed for conversation {ConvId}", conversationId);
        }
    }

    private async Task<string> ExtractFactsAsync(string conversationText, CancellationToken ct)
    {
        var prompt = $"""
            Analyze the following conversation excerpt and extract key information.

            Produce:
            1. **Facts established** (bulleted): user preferences, user-stated constraints, entities introduced
            2. **Decisions made** (bulleted): anything explicitly decided or approved
            3. **Work completed** (bulleted): artifacts produced, tasks dispatched, outcomes confirmed
            4. **Narrative summary** (2-4 sentences): the arc of what was covered

            IMPORTANT: Discard pleasantries, acknowledgments, minor clarifications, and repeated information.
            Only extract facts, decisions, and completed work that would be useful in future turns.

            CONVERSATION:
            {conversationText}
            """;

        var request = new ConverseRequest
        {
            ModelId = ExtractionModel,
            Messages =
            [
                new BedrockMessage
                {
                    Role = ConversationRole.User,
                    Content = [new BedrockContentBlock { Text = prompt }]
                }
            ]
        };

        var response = await _bedrockRuntime.ConverseAsync(request, ct);
        return response.Output.Message.Content.FirstOrDefault()?.Text
            ?? "[Compaction extraction returned empty output]";
    }

    private async Task RecalculateTokenCountAsync(string conversationId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);

        var totalTokens = await db.Messages
            .Where(m => m.ConversationId == conversationId
                     && (m.IsCompactionSummary || m.CompactedAt == null))
            .SumAsync(m => m.TokenCount, cts.Token);

        await db.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EstimatedTokenCount, totalTokens), cts.Token);
    }

    private static int EstimateTokens(string text) => (int)Math.Ceiling(text.Length / 4.0);
}
