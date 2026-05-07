namespace FortressAI.V2.Web.Services;

/// <summary>
/// RAG write path — pgvector chunk persistence and memory file merges.
/// pgvector writes are synchronous (available next turn); memory file merges are async/queued.
/// </summary>
public interface IRAGWriteService
{
    // TODO Sprint 3: implement extraction pipeline (LLM fact extraction from conversation range)
    Task QueueExtractionAsync(string conversationId, string messageRangeHint, CancellationToken ct = default);

    // TODO Sprint 3: synchronous pgvector upsert — chunk must be queryable on next turn
    Task WriteFactAsync(MemoryChunk chunk, CancellationToken ct = default);

    // TODO Sprint 3: async background queue — merge new facts into user's topic .md file
    Task MergeToMemoryFileAsync(string userId, string topicSlug, IEnumerable<string> newFacts, CancellationToken ct = default);
}

/// <summary>
/// A single vector-embedded memory chunk for pgvector storage.
/// </summary>
public record MemoryChunk(
    string UserId,
    string TopicSlug,
    string Content,
    string Source,
    DateTimeOffset CreatedAt
);
