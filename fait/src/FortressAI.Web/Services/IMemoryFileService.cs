using FortressAI.Shared.Models;

namespace FortressAI.Web.Services;

public interface IMemoryFileService
{
    /// <summary>Returns all memory_topics rows for user, ordered by Title.</summary>
    Task<List<MemoryTopic>> GetTopicsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Reads topic .md file from S3. Returns null if not found.</summary>
    Task<string?> GetTopicContentAsync(Guid userId, string slug, CancellationToken ct = default);

    /// <summary>Writes .md to S3, upserts memory_topics row, rebuilds MEMORY.md index.</summary>
    Task WriteTopicAsync(Guid userId, string slug, string title, string content, CancellationToken ct = default);

    /// <summary>Deletes .md from S3, removes memory_topics row, rebuilds MEMORY.md index.</summary>
    Task DeleteTopicAsync(Guid userId, string slug, CancellationToken ct = default);

    /// <summary>Regenerates MEMORY.md from current memory_topics rows and writes to S3.</summary>
    Task RebuildMemoryIndexAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Returns a ZIP stream of all topic .md files + MEMORY.md for this user.</summary>
    Task<Stream> ExportZipAsync(Guid userId, CancellationToken ct = default);
}
