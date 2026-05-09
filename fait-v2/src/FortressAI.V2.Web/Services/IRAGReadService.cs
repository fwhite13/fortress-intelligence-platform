namespace FortressAI.V2.Web.Services;

/// <summary>
/// RAG read path — pgvector similarity search and cold-start warmup.
/// </summary>
public interface IRAGReadService
{
    // TODO Sprint 3: fire real pgvector no-op query to warm connection pool on cold start (OQ-15-2)
    Task WarmupAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Semantic similarity search against the user's memory chunks.
    /// Embeds the query via Bedrock Titan, then runs cosine similarity against pgvector.
    /// Returns top-K results ordered by similarity descending.
    /// Never throws — returns empty list on error.
    /// </summary>
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        string userId, string query, int topK = 5, CancellationToken ct = default);
}

/// <summary>
/// A single memory chunk returned from pgvector similarity search.
/// </summary>
public record MemorySearchResult(string TopicSlug, string Source, string Content, double Similarity);
