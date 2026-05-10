using System.IO.Compression;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Services;

public class MemoryFileService : IMemoryFileService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<MemoryFileService> _logger;

    private string BucketName => _config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";

    private static string TopicKey(Guid userId, string slug) =>
        $"workspaces/{userId}/memory/{slug}.md";

    private static string IndexKey(Guid userId) =>
        $"workspaces/{userId}/memory/MEMORY.md";

    public MemoryFileService(
        IDbContextFactory<AppDbContext> dbFactory,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<MemoryFileService> logger)
    {
        _dbFactory = dbFactory;
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    public async Task<List<MemoryTopic>> GetTopicsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.MemoryTopics
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Title)
            .ToListAsync(ct);
    }

    public async Task<string?> GetTopicContentAsync(Guid userId, string slug, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(BucketName, TopicKey(userId, slug), ct);
            using var reader = new StreamReader(response.ResponseStream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return null;
        }
    }

    public async Task WriteTopicAsync(Guid userId, string slug, string title, string content, CancellationToken ct = default)
    {
        // 1. Write content to S3
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = TopicKey(userId, slug),
            ContentBody = content
        }, ct);
        _logger.LogDebug("[MemoryFile] Wrote s3://{Bucket}/{Key}", BucketName, TopicKey(userId, slug));

        // 2. Upsert memory_topics row
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.MemoryTopics
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Slug == slug, ct);

        if (existing != null)
        {
            existing.Title = title;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.MemoryTopics.Add(new MemoryTopic
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Slug = slug,
                Title = title,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(ct);

        // 3. Rebuild MEMORY.md index
        await RebuildMemoryIndexAsync(userId, ct);
    }

    public async Task DeleteTopicAsync(Guid userId, string slug, CancellationToken ct = default)
    {
        // 1. Delete from S3 (ignore NoSuchKey)
        try
        {
            await _s3.DeleteObjectAsync(BucketName, TopicKey(userId, slug), ct);
            _logger.LogDebug("[MemoryFile] Deleted s3://{Bucket}/{Key}", BucketName, TopicKey(userId, slug));
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            // Already gone — that's fine
        }

        // 2. Remove DB row
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.MemoryTopics
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Slug == slug, ct);

        if (existing != null)
        {
            db.MemoryTopics.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        // 3. Rebuild index
        await RebuildMemoryIndexAsync(userId, ct);
    }

    public async Task RebuildMemoryIndexAsync(Guid userId, CancellationToken ct = default)
    {
        var topics = await GetTopicsAsync(userId, ct);

        var sb = new StringBuilder();
        sb.AppendLine("# Memory Index");
        sb.AppendLine($"_Last updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC_");
        sb.AppendLine();
        sb.AppendLine("## Topics");
        foreach (var t in topics) // already ordered by title
            sb.AppendLine($"- [{t.Title}]({t.Slug}.md)");

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = IndexKey(userId),
            ContentBody = sb.ToString()
        }, ct);
        _logger.LogDebug("[MemoryFile] Rebuilt MEMORY.md index for user {UserId} ({Count} topics)", userId, topics.Count);
    }

    public async Task<Stream> ExportZipAsync(Guid userId, CancellationToken ct = default)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Add each topic file
            var topics = await GetTopicsAsync(userId, ct);
            foreach (var topic in topics)
            {
                var content = await GetTopicContentAsync(userId, topic.Slug, ct);
                if (content == null) continue;

                var entry = zip.CreateEntry($"{topic.Slug}.md");
                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream);
                await writer.WriteAsync(content);
            }

            // Add MEMORY.md index
            try
            {
                var indexResponse = await _s3.GetObjectAsync(BucketName, IndexKey(userId), ct);
                using var reader = new StreamReader(indexResponse.ResponseStream);
                var indexContent = await reader.ReadToEndAsync(ct);
                var indexEntry = zip.CreateEntry("MEMORY.md");
                await using var indexStream = indexEntry.Open();
                await using var indexWriter = new StreamWriter(indexStream);
                await indexWriter.WriteAsync(indexContent);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
            {
                // No index yet — skip
            }
        }
        ms.Position = 0;
        return ms;
    }
}
