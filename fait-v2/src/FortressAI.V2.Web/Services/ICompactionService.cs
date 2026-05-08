namespace FortressAI.V2.Web.Services;

public interface ICompactionService
{
    Task<bool> ShouldCompactAsync(string conversationId, CancellationToken ct = default);
    Task CompactIfNeededAsync(string conversationId, CancellationToken ct = default);
}
