namespace FortressAI.V2.Web.Services;

/// <summary>
/// Sprint 2 stub for IRAGWriteService. Logs operations only; no actual pgvector writes.
/// Replace in Sprint 3 with full implementation.
/// </summary>
public class RAGWriteServiceStub : IRAGWriteService
{
    private readonly ILogger<RAGWriteServiceStub> _logger;

    public RAGWriteServiceStub(ILogger<RAGWriteServiceStub> logger) => _logger = logger;

    public Task QueueExtractionAsync(string conversationId, string messageRangeHint, CancellationToken ct = default)
    {
        _logger.LogInformation("[RAGWrite STUB] QueueExtraction: conversation={ConvId} range={Range}", conversationId, messageRangeHint);
        return Task.CompletedTask;
    }

    public Task WriteFactAsync(MemoryChunk chunk, CancellationToken ct = default)
    {
        _logger.LogInformation("[RAGWrite STUB] WriteFact: source={Source} topic={Topic} content={Content}",
            chunk.Source, chunk.TopicSlug, chunk.Content.Length > 100 ? chunk.Content[..100] + "..." : chunk.Content);
        return Task.CompletedTask;
    }

    public Task MergeToMemoryFileAsync(string userId, string topicSlug, IEnumerable<string> newFacts, CancellationToken ct = default)
    {
        _logger.LogInformation("[RAGWrite STUB] MergeToMemoryFile: user={UserId} topic={TopicSlug} facts={FactCount}",
            userId, topicSlug, newFacts.Count());
        return Task.CompletedTask;
    }
}
