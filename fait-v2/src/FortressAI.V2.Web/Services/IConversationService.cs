using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public interface IConversationService
{
    Task<Conversation> GetOrCreateActiveConversationAsync(string userId, CancellationToken ct = default);
    Task<List<Message>> GetMessagesForContextAsync(string conversationId, CancellationToken ct = default);
    Task<Message> AppendMessageAsync(string conversationId, string role, string content, int tokenCount = 0, CancellationToken ct = default);
    Task<List<Message>> GetFullHistoryAsync(string conversationId, CancellationToken ct = default);
    Task UpdateTokenCountAsync(string conversationId, int delta, CancellationToken ct = default);
}
