using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

public class ChatService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly BedrockService _bedrockService;
    private readonly ILogger<ChatService> _logger;

    // Token budget constants
    private const int TotalTokenBudget = 180_000;
    private const int SystemPromptReserve = 20_000;  // Reserve for project instructions + documents
    private const int ResponseReserve = 10_000;       // Reserve for model response
    private const int ConversationBudget = TotalTokenBudget - SystemPromptReserve - ResponseReserve; // 150K for conversation
    private const int RecentMessagesToKeep = 20;
    private const string SummaryModelId = "claude-haiku-4-5";

    public ChatService(IDbContextFactory<AppDbContext> contextFactory, BedrockService bedrockService, ILogger<ChatService> logger)
    {
        _contextFactory = contextFactory;
        _bedrockService = bedrockService;
        _logger = logger;
    }

    public async Task<Conversation> CreateConversationAsync(Guid userId, Guid? projectId, string model = "claude-sonnet-4-6")
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        string? defaultModel = model;
        if (projectId.HasValue)
        {
            var project = await db.Projects.FindAsync(projectId.Value);
            if (project != null)
                defaultModel = project.Model;
        }

        var conversation = new Conversation
        {
            UserId = userId,
            ProjectId = projectId,
            Model = defaultModel ?? "claude-sonnet-4-6",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    public async Task<Conversation?> GetConversationAsync(Guid conversationId, Guid userId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Conversations
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .Include(c => c.TeamKbs)
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);
    }

    public async Task<List<Conversation>> GetUserConversationsAsync(Guid userId, int limit = 50)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Conversations
            .Where(c => c.UserId == userId && c.Messages.Any())
            .Include(c => c.Project)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<Conversation>> SearchConversationsAsync(Guid userId, string query, int limit = 50)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        List<Guid> contentMatchIds;
        try
        {
            // Try FULLTEXT search first — scoped to current user
            contentMatchIds = await db.Messages
                .FromSqlRaw("SELECT * FROM messages WHERE MATCH(Content) AGAINST({0} IN NATURAL LANGUAGE MODE)", query)
                .Where(m => m.Conversation!.UserId == userId)
                .Select(m => m.ConversationId)
                .Distinct()
                .ToListAsync();
        }
        catch
        {
            // Fall back to LIKE — scoped to current user
            contentMatchIds = await db.Messages
                .Where(m => m.Content.Contains(query))
                .Where(m => m.Conversation!.UserId == userId)
                .Select(m => m.ConversationId)
                .Distinct()
                .ToListAsync();
        }

        var titleMatchIds = await db.Conversations
            .Where(c => c.UserId == userId && c.Title != null && EF.Functions.Like(c.Title, $"%{query}%"))
            .Select(c => c.Id)
            .ToListAsync();

        var allIds = contentMatchIds.Union(titleMatchIds).ToHashSet();

        return await db.Conversations
            .Where(c => c.UserId == userId && allIds.Contains(c.Id) && c.Messages.Any())
            .Include(c => c.Project)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<Conversation>> GetProjectConversationsAsync(Guid projectId, Guid userId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Conversations
            .Where(c => c.ProjectId == projectId && c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();
    }

    public async Task<ChatMessage> AddMessageAsync(Guid conversationId, string role, string content, string? model = null, int? tokensIn = null, int? tokensOut = null)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var message = new ChatMessage
        {
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Model = model,
            TokensIn = tokensIn,
            TokensOut = tokensOut,
            CreatedAt = DateTime.UtcNow
        };

        db.Messages.Add(message);

        // Update conversation timestamp and title
        var conversation = await db.Conversations.FindAsync(conversationId);
        if (conversation != null)
        {
            conversation.UpdatedAt = DateTime.UtcNow;
            if (string.IsNullOrEmpty(conversation.Title) && role == "user")
            {
                conversation.Title = content.Length > 100 ? content[..100] + "..." : content;
            }
        }

        await db.SaveChangesAsync();
        return message;
    }

    public async Task<bool> DeleteConversationAsync(Guid conversationId, Guid userId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);
        if (conversation == null) return false;

        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task UpdateConversationModelAsync(Guid conversationId, string model)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var conversation = await db.Conversations.FindAsync(conversationId);
        if (conversation != null)
        {
            conversation.Model = model;
            await db.SaveChangesAsync();
        }
    }

    public async Task UpdateConversationKbAsync(Guid conversationId, bool enableFortressKb, bool enablePersonalKb, List<int>? teamKbIds = null)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var conversation = await db.Conversations.FindAsync(conversationId);
        if (conversation == null) return;

        conversation.EnableFortressKb = enableFortressKb;
        conversation.EnablePersonalKb = enablePersonalKb;
        conversation.UpdatedAt = DateTime.UtcNow;

        // Update team KBs — delete all existing, insert new ones
        var existing = await db.ConversationTeamKbs
            .Where(ct => ct.ConversationId == conversationId)
            .ToListAsync();
        db.ConversationTeamKbs.RemoveRange(existing);

        if (teamKbIds?.Any() == true)
        {
            foreach (var teamId in teamKbIds)
            {
                db.ConversationTeamKbs.Add(new ConversationTeamKb
                {
                    ConversationId = conversationId,
                    TeamId = teamId,
                    EnabledAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task UpdateConversationWorkingFolderAsync(Guid conversationId, Guid? workingFolderId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId);
        if (conversation == null) return;
        conversation.WorkingFolderId = workingFolderId;
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Estimates token count for a string using rough chars/4 approximation.
    /// </summary>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length / 4;
    }

    /// <summary>
    /// Prepares messages for sending to Bedrock, applying sliding window with summary
    /// if the conversation exceeds the token budget.
    /// Returns (messages, wasSummarized).
    /// </summary>
    public async Task<(List<MessageDto> Messages, bool WasSummarized)> PrepareMessagesWithSlidingWindowAsync(
        List<ChatMessage> allMessages,
        string? systemPrompt)
    {
        var conversationMessages = allMessages
            .Where(m => m.Role is "user" or "assistant")
            .OrderBy(m => m.CreatedAt)
            .ToList();

        // Calculate total conversation tokens
        int totalTokens = conversationMessages.Sum(m => EstimateTokens(m.Content));

        // If within budget, return all messages as-is
        if (totalTokens <= ConversationBudget)
        {
            var msgs = conversationMessages
                .Select(m => new MessageDto { Role = m.Role, Content = m.Content })
                .ToList();
            return (msgs, false);
        }

        _logger.LogInformation(
            "Conversation exceeds token budget ({Tokens} > {Budget}). Applying sliding window with summary.",
            totalTokens, ConversationBudget);

        // Split: keep last N messages, summarize the rest
        int recentCount = Math.Min(RecentMessagesToKeep, conversationMessages.Count);
        var olderMessages = conversationMessages.Take(conversationMessages.Count - recentCount).ToList();
        var recentMessages = conversationMessages.Skip(conversationMessages.Count - recentCount).ToList();

        // Generate summary of older messages using Haiku
        string summary = await GenerateConversationSummaryAsync(olderMessages);

        // Build the result: summary message + recent messages
        var result = new List<MessageDto>();

        // Add summary as a user message with clear framing
        result.Add(new MessageDto
        {
            Role = "user",
            Content = $"[Previous conversation summary — {olderMessages.Count} messages summarized]\n\n{summary}"
        });

        // Add a brief assistant acknowledgment so the conversation alternation is maintained
        result.Add(new MessageDto
        {
            Role = "assistant",
            Content = "Understood. I have the context from our previous conversation. Let's continue."
        });

        // Add recent messages
        foreach (var msg in recentMessages)
        {
            result.Add(new MessageDto { Role = msg.Role, Content = msg.Content });
        }

        _logger.LogInformation(
            "Sliding window applied: {OlderCount} messages summarized, {RecentCount} messages kept. Summary tokens: ~{SummaryTokens}",
            olderMessages.Count, recentMessages.Count, EstimateTokens(summary));

        return (result, true);
    }

    /// <summary>
    /// Uses Bedrock Haiku to generate a concise summary of older conversation messages.
    /// </summary>
    private async Task<string> GenerateConversationSummaryAsync(List<ChatMessage> messages)
    {
        var conversationText = new System.Text.StringBuilder();
        foreach (var msg in messages)
        {
            var role = msg.Role == "user" ? "User" : "Assistant";
            // Truncate very long messages in the summary input
            var content = msg.Content.Length > 2000 ? msg.Content[..2000] + "..." : msg.Content;
            conversationText.AppendLine($"{role}: {content}");
            conversationText.AppendLine();
        }

        var summaryPrompt = new List<MessageDto>
        {
            new()
            {
                Role = "user",
                Content = $"""
                    Summarize the following conversation concisely. Focus on:
                    - Key topics discussed
                    - Important decisions or conclusions reached
                    - Any specific facts, numbers, or details that were established
                    - Outstanding questions or tasks
                    
                    Keep the summary under 500 words. Be factual and precise.
                    
                    Conversation:
                    {conversationText}
                    """
            }
        };

        var fullResponse = new System.Text.StringBuilder();

        try
        {
            await foreach (var chunk in _bedrockService.StreamChatAsync(
                summaryPrompt,
                "You are a conversation summarizer. Produce concise, factual summaries.",
                SummaryModelId,
                maxTokens: 1024,
                temperature: 0.3))
            {
                if (chunk.Type == "text" && chunk.Text != null)
                {
                    fullResponse.Append(chunk.Text);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate conversation summary via Haiku. Using fallback.");
            // Fallback: simple truncation-based summary
            return GenerateFallbackSummary(messages);
        }

        var result = fullResponse.ToString().Trim();
        if (string.IsNullOrEmpty(result))
        {
            return GenerateFallbackSummary(messages);
        }

        return result;
    }

    /// <summary>
    /// Generates a contextual title for a conversation from its first user+assistant exchange.
    /// Should only be called once per conversation (when messages.Count == 2 in ChatView).
    /// The title guard (IsNullOrEmpty) does not prevent overwriting a previously generated title —
    /// callers are responsible for ensuring single invocation.
    /// </summary>
    public async Task GenerateConversationTitleAsync(Guid conversationId, string userMessage, string assistantResponse)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var conversation = await db.Conversations.FindAsync(conversationId);
        if (conversation == null) return;

        // Only generate if title is currently set (i.e., the placeholder from AddMessageAsync was written)
        if (string.IsNullOrEmpty(conversation.Title)) return;

        var prompt = $"""
            Generate a short, descriptive title (3–6 words) for this conversation based on the first exchange.
            Do not use quotes. Do not use punctuation at the end. Just the title, nothing else.

            User: {userMessage[..Math.Min(300, userMessage.Length)]}
            Assistant: {assistantResponse[..Math.Min(300, assistantResponse.Length)]}

            Title:
            """;

        try
        {
            var title = await _bedrockService.GenerateTitleAsync(prompt);
            if (!string.IsNullOrWhiteSpace(title))
            {
                conversation.Title = title.Trim().TrimEnd('.', '!', '?');
                conversation.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Title generation failed for conversation {Id} — keeping raw title", conversationId);
        }
    }

    /// <summary>
    /// Fallback summary when Bedrock call fails — just list the first line of each message.
    /// </summary>
    private static string GenerateFallbackSummary(List<ChatMessage> messages)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Previous conversation topics:");
        foreach (var msg in messages.Where(m => m.Role == "user").Take(10))
        {
            var firstLine = msg.Content.Split('\n').FirstOrDefault()?.Trim() ?? "";
            if (firstLine.Length > 100) firstLine = firstLine[..100] + "...";
            sb.AppendLine($"- {firstLine}");
        }
        return sb.ToString().Trim();
    }
}
