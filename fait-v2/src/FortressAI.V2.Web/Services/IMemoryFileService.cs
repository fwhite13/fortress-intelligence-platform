using System.IO.Compression;

namespace FortressAI.V2.Web.Services;

/// <summary>
/// Info about a memory file stored in S3.
/// </summary>
public record MemoryFileInfo(string FileName, long SizeBytes, DateTimeOffset LastModified);

/// <summary>
/// A user's memory topic backed by an S3 file.
/// </summary>
public record MemoryTopicEntry(string TopicSlug, string Content, DateTimeOffset LastModified);

/// <summary>
/// Manages per-user memory files and topics in S3.
/// S3 prefixes:
///   General files:  workspaces/{userId}/memory/{fileName}
///   Topic files:    workspaces/{userId}/memory/topics/{topicSlug}.md
/// </summary>
public interface IMemoryFileService
{
    // ── S3 file operations ──────────────────────────────────────────────

    /// <summary>Reads a memory file. Returns null if not found.</summary>
    Task<string?> ReadFileAsync(string userId, string fileName, CancellationToken ct = default);

    /// <summary>Writes (creates or overwrites) a memory file.</summary>
    Task WriteFileAsync(string userId, string fileName, string content, CancellationToken ct = default);

    /// <summary>Deletes a memory file. No-op if not found.</summary>
    Task DeleteFileAsync(string userId, string fileName, CancellationToken ct = default);

    /// <summary>Lists all memory files for a user (under workspaces/{userId}/memory/).</summary>
    Task<IReadOnlyList<MemoryFileInfo>> ListFilesAsync(string userId, CancellationToken ct = default);

    // ── Topic CRUD ─────────────────────────────────────────────────────

    /// <summary>Lists all topic slugs for a user.</summary>
    Task<IReadOnlyList<MemoryTopicEntry>> GetTopicsAsync(string userId, CancellationToken ct = default);

    /// <summary>Gets a single topic. Returns null if not found.</summary>
    Task<MemoryTopicEntry?> GetTopicAsync(string userId, string topicSlug, CancellationToken ct = default);

    /// <summary>Creates or updates a topic file in S3.</summary>
    Task<MemoryTopicEntry> UpsertTopicAsync(string userId, string topicSlug, string content, CancellationToken ct = default);

    /// <summary>Deletes a topic file from S3. No-op if not found.</summary>
    Task DeleteTopicAsync(string userId, string topicSlug, CancellationToken ct = default);

    // ── Export ─────────────────────────────────────────────────────────

    /// <summary>Exports all files under workspaces/{userId}/memory/ as a ZIP archive.</summary>
    Task<byte[]> ExportZipAsync(string userId, CancellationToken ct = default);
}
