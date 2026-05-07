namespace FortressAI.V2.Web.Services;

/// <summary>
/// RAG read path — pgvector similarity search and cold-start warmup.
/// </summary>
public interface IRAGReadService
{
    // TODO Sprint 3: fire real pgvector no-op query to warm connection pool on cold start (OQ-15-2)
    Task WarmupAsync(string userId, CancellationToken ct = default);

    // TODO Sprint 3: pgvector similarity search for user memory retrieval
    Task<IReadOnlyList<MemoryChunk>> RetrieveAsync(string userId, string query, CancellationToken ct = default);
}
