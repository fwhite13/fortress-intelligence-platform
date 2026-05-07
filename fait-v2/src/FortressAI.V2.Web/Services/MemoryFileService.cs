using Amazon.S3;
using Amazon.S3.Model;
using System.IO.Compression;

namespace FortressAI.V2.Web.Services;

/// <summary>
/// S3-backed implementation of IMemoryFileService.
/// All memory files are stored under workspaces/{userId}/memory/.
/// Topic files are stored at workspaces/{userId}/memory/topics/{topicSlug}.md.
/// Credentials come from the ECS task role — no hardcoded keys.
/// </summary>
public class MemoryFileService : IMemoryFileService
{
    private readonly IAmazonS3 _s3;
    private readonly ILogger<MemoryFileService> _logger;
    private readonly string _bucket;

    public MemoryFileService(
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<MemoryFileService> logger)
    {
        _s3 = s3;
        _logger = logger;
        _bucket = config["AWS:WorkspaceBucket"]
            ?? throw new InvalidOperationException("AWS:WorkspaceBucket is not configured.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string FileKey(string userId, string fileName) =>
        $"workspaces/{userId}/memory/{fileName}";

    private static string TopicKey(string userId, string topicSlug) =>
        $"workspaces/{userId}/memory/topics/{topicSlug}.md";

    private static string MemoryPrefix(string userId) =>
        $"workspaces/{userId}/memory/";

    private static string TopicsPrefix(string userId) =>
        $"workspaces/{userId}/memory/topics/";

    // ── S3 file operations ────────────────────────────────────────────────

    public async Task<string?> ReadFileAsync(string userId, string fileName, CancellationToken ct = default)
    {
        var key = FileKey(userId, fileName);
        try
        {
            var response = await _s3.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucket,
                Key = key
            }, ct);
            using var reader = new StreamReader(response.ResponseStream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Memory file not found: {Key}", key);
            return null;
        }
    }

    public async Task WriteFileAsync(string userId, string fileName, string content, CancellationToken ct = default)
    {
        var key = FileKey(userId, fileName);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            ContentBody = content,
            ContentType = "text/markdown"
        }, ct);
        _logger.LogInformation("Wrote memory file {Key} ({Bytes} bytes)", key, content.Length);
    }

    public async Task DeleteFileAsync(string userId, string fileName, CancellationToken ct = default)
    {
        var key = FileKey(userId, fileName);
        try
        {
            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = key
            }, ct);
            _logger.LogInformation("Deleted memory file {Key}", key);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Memory file already absent: {Key}", key);
        }
    }

    public async Task<IReadOnlyList<MemoryFileInfo>> ListFilesAsync(string userId, CancellationToken ct = default)
    {
        var prefix = MemoryPrefix(userId);
        var results = new List<MemoryFileInfo>();
        string? continuationToken = null;

        do
        {
            var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken
            }, ct);

            foreach (var obj in response.S3Objects)
            {
                // Strip the prefix to get the relative fileName
                var relName = obj.Key[prefix.Length..];
                if (!string.IsNullOrEmpty(relName))
                    results.Add(new MemoryFileInfo(relName, obj.Size, obj.LastModified));
            }

            continuationToken = response.IsTruncated ? response.NextContinuationToken : null;
        }
        while (continuationToken != null);

        return results.AsReadOnly();
    }

    // ── Topic CRUD ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MemoryTopicEntry>> GetTopicsAsync(string userId, CancellationToken ct = default)
    {
        var prefix = TopicsPrefix(userId);
        var topics = new List<MemoryTopicEntry>();
        string? continuationToken = null;

        do
        {
            var listResponse = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken
            }, ct);

            foreach (var obj in listResponse.S3Objects)
            {
                // Extract topicSlug from key: ...topics/{topicSlug}.md
                var keyName = obj.Key[prefix.Length..];
                if (!keyName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                var slug = keyName[..^3]; // strip .md

                // Fetch content
                var getResponse = await _s3.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = _bucket,
                    Key = obj.Key
                }, ct);
                using var reader = new StreamReader(getResponse.ResponseStream);
                var content = await reader.ReadToEndAsync(ct);

                topics.Add(new MemoryTopicEntry(slug, content, obj.LastModified));
            }

            continuationToken = listResponse.IsTruncated ? listResponse.NextContinuationToken : null;
        }
        while (continuationToken != null);

        return topics.AsReadOnly();
    }

    public async Task<MemoryTopicEntry?> GetTopicAsync(string userId, string topicSlug, CancellationToken ct = default)
    {
        var key = TopicKey(userId, topicSlug);
        try
        {
            var response = await _s3.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucket,
                Key = key
            }, ct);
            using var reader = new StreamReader(response.ResponseStream);
            var content = await reader.ReadToEndAsync(ct);
            var lastModified = response.LastModified;
            return new MemoryTopicEntry(topicSlug, content, lastModified);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Topic not found: {Key}", key);
            return null;
        }
    }

    public async Task<MemoryTopicEntry> UpsertTopicAsync(string userId, string topicSlug, string content, CancellationToken ct = default)
    {
        var key = TopicKey(userId, topicSlug);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            ContentBody = content,
            ContentType = "text/markdown"
        }, ct);

        var now = DateTimeOffset.UtcNow;
        _logger.LogInformation("Upserted topic {Slug} for user {UserId} at {Key}", topicSlug, userId, key);

        // Sync to pgvector index (stub — Sprint 2)
        await SyncToVectorIndexAsync(userId, topicSlug, content);

        return new MemoryTopicEntry(topicSlug, content, now);
    }

    public async Task DeleteTopicAsync(string userId, string topicSlug, CancellationToken ct = default)
    {
        var key = TopicKey(userId, topicSlug);
        try
        {
            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = key
            }, ct);
            _logger.LogInformation("Deleted topic {Slug} for user {UserId}", topicSlug, userId);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Topic already absent: {Key}", key);
        }
    }

    // ── Export ────────────────────────────────────────────────────────────

    public async Task<byte[]> ExportZipAsync(string userId, CancellationToken ct = default)
    {
        var prefix = MemoryPrefix(userId);
        var files = new List<(string RelativePath, byte[] Data)>();
        string? continuationToken = null;

        // Collect all keys under workspaces/{userId}/memory/
        do
        {
            var listResponse = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken
            }, ct);

            foreach (var obj in listResponse.S3Objects)
            {
                var relPath = obj.Key[prefix.Length..];
                if (string.IsNullOrEmpty(relPath)) continue;

                var getResponse = await _s3.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = _bucket,
                    Key = obj.Key
                }, ct);

                using var ms = new MemoryStream();
                await getResponse.ResponseStream.CopyToAsync(ms, ct);
                files.Add((relPath, ms.ToArray()));
            }

            continuationToken = listResponse.IsTruncated ? listResponse.NextContinuationToken : null;
        }
        while (continuationToken != null);

        // Build ZIP in memory
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (relPath, data) in files)
            {
                var entry = archive.CreateEntry(relPath, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(data, ct);
            }
        }

        _logger.LogInformation("Exported {Count} memory files for user {UserId} as ZIP ({Bytes} bytes)",
            files.Count, userId, zipStream.Length);

        return zipStream.ToArray();
    }

    // ── pgvector stub ─────────────────────────────────────────────────────

    /// <summary>
    /// Syncs a topic to the pgvector index.
    /// TODO: wire pgvector sync via PostgresMemoryService (Sprint 2 #2847 follow-up)
    /// </summary>
    private static Task SyncToVectorIndexAsync(string userId, string topicSlug, string content)
    {
        // No-op stub — pgvector integration is Sprint 2
        return Task.CompletedTask;
    }
}
