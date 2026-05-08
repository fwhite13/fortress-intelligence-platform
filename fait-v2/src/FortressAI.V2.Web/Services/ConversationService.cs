using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class ConversationService : IConversationService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(IDbContextFactory<FaitV2DbContext> dbFactory, ILogger<ConversationService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<Conversation> GetOrCreateActiveConversationAsync(string userId, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);

        var existing = await db.Conversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.LastActiveAt)
            .FirstOrDefaultAsync(cts.Token);

        if (existing != null)
            return existing;

        var conv = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            EstimatedTokenCount = 0
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync(cts.Token);
        _logger.LogInformation("Created new conversation {ConvId} for user {UserId}", conv.Id, userId);
        return conv;
    }

    public async Task<List<Message>> GetMessagesForContextAsync(string conversationId, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);

        var messages = await db.Messages
            .Where(m => m.ConversationId == conversationId &&
                        (m.IsCompactionSummary || m.CompactedAt == null))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cts.Token);

        return messages;
    }

    public async Task<Message> AppendMessageAsync(string conversationId, string role, string content, int tokenCount = 0, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);

        var msg = new Message
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            TokenCount = tokenCount,
            CreatedAt = DateTime.UtcNow,
            SessionType = "main"
        };
        db.Messages.Add(msg);

        await db.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastActiveAt, DateTime.UtcNow), cts.Token);

        await db.SaveChangesAsync(cts.Token);
        return msg;
    }

    public async Task<List<Message>> GetFullHistoryAsync(string conversationId, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);

        return await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cts.Token);
    }

    public async Task UpdateTokenCountAsync(string conversationId, int delta, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE conversations SET estimated_token_count = estimated_token_count + {0} WHERE id = {1}",
            new object[] { delta, conversationId },
            cts.Token);
    }
}
