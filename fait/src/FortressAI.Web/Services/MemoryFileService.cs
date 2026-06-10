using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
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
    private readonly IAmazonBedrockRuntime _bedrock;
    private readonly IConfiguration _config;
    private readonly ILogger<MemoryFileService> _logger;

    private string BucketName => _config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";

    private static string TopicKey(Guid userId, string slug) =>
        $"workspaces/{userId}/memory/{slug}.md";

    private static string IndexKey(Guid userId) =>
        $"workspaces/{userId}/memory/MEMORY.md";

    private static string AssistantKey(Guid userId, string filename) =>
        $"workspaces/{userId}/assistants/{filename}";

    public MemoryFileService(
        IDbContextFactory<AppDbContext> dbFactory,
        IAmazonS3 s3,
        IAmazonBedrockRuntime bedrock,
        IConfiguration config,
        ILogger<MemoryFileService> logger)
    {
        _dbFactory = dbFactory;
        _s3 = s3;
        _bedrock = bedrock;
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
        if (slug.Equals("MEMORY", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The slug 'MEMORY' is reserved.", nameof(slug));

        // 1. Write content to S3
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = TopicKey(userId, slug),
            ContentBody = content
        }, ct);
        _logger.LogDebug("[MemoryFile] Wrote s3://{Bucket}/{Key}", BucketName, TopicKey(userId, slug));

        // 2. Upsert memory_topics row
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
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
        } // db disposed here

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
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            var topic = await db.MemoryTopics
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Slug == slug, ct);

            if (topic != null)
            {
                db.MemoryTopics.Remove(topic);
                await db.SaveChangesAsync(ct);
            }
        } // disposed before Rebuild

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

            if (topics.Count == 0)
            {
                // Fall back to S3 listing (paginated — matches codebase pattern)
                var listReq = new ListObjectsV2Request
                {
                    BucketName = BucketName,
                    Prefix = $"workspaces/{userId}/memory/"
                };
                ListObjectsV2Response listResp;
                do
                {
                    listResp = await _s3.ListObjectsV2Async(listReq, ct);
                    foreach (var s3obj in listResp.S3Objects)
                    {
                        if (!s3obj.Key.EndsWith(".md")) continue;
                        if (s3obj.Key.Split('/').Last().Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase)) continue; // handled by dedicated block below
                        var filename = s3obj.Key.Split('/').Last();
                        try
                        {
                            var response = await _s3.GetObjectAsync(BucketName, s3obj.Key, ct);
                            using var reader = new StreamReader(response.ResponseStream);
                            var content = await reader.ReadToEndAsync(ct);
                            var entry = zip.CreateEntry(filename);
                            await using var entryStream = entry.Open();
                            await using var writer = new StreamWriter(entryStream);
                            await writer.WriteAsync(content);
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // propagate cancellation — do not swallow
                        }
                        catch (AmazonS3Exception ex) when (ex.ErrorCode is "NoSuchKey" or "AccessDenied")
                        {
                            _logger.LogWarning("[MemoryFile] Skipping {Key}: {ErrorCode}", s3obj.Key, ex.ErrorCode);
                        }
                        // All other exceptions propagate — systemic failures (throttling, expired creds) surface as 500
                    }
                    listReq.ContinuationToken = listResp.NextContinuationToken;
                } while (listResp.IsTruncated);
            }
            else
            {
                foreach (var topic in topics)
                {
                    var content = await GetTopicContentAsync(userId, topic.Slug, ct);
                    if (content == null) continue;

                    var entry = zip.CreateEntry($"{topic.Slug}.md");
                    await using var entryStream = entry.Open();
                    await using var writer = new StreamWriter(entryStream);
                    await writer.WriteAsync(content);
                }
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

    public async Task<ImportMemoryResult> ImportMemoryAsync(Guid userId, string content, CancellationToken ct = default)
    {
        const int MaxContentChars = 50_000;
        if (content.Length > MaxContentChars)
            throw new ArgumentException($"Content too large (max {MaxContentChars} chars).");

        // Strip wrapper formats (code fences and banner lines)
        var stripped = content.Trim();
        if (stripped.StartsWith("```"))
        {
            var firstNewline = stripped.IndexOf('\n');
            var lastFence = stripped.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
                stripped = stripped[(firstNewline + 1)..lastFence].Trim();
        }
        stripped = Regex.Replace(stripped, @"^={5,}.*$", "", RegexOptions.Multiline).Trim();

        // Bedrock 4-bucket classification
        var modelId = _config["BEDROCK_MODEL_ID"] ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0";
        const string systemPrompt = @"You are a memory routing classifier. Given imported content from another AI system, classify it into four categories. Be thorough — don't leave content unrouted if it fits a category.

1. soulMd: How the AI should behave with this user. Tone, communication style, response format, what the user finds helpful vs. annoying, inferred working preferences, things the AI learned about how to interact with this person. This is behavioral/persona guidance — what a new AI assistant needs to know to behave correctly with this user.

2. agentsMd: Explicit rules and corrections. Things the user explicitly told the AI to always do or never do. Corrections to AI behavior. Format requirements. Constraints. Preserve the user's exact words where possible.

3. userMd: Personal facts about the user themselves. Name, role, employer, family, location, background, personal interests, lifestyle facts. Facts that apply across all conversations.

4. topics: An array of { topic: string, content: string } for everything else — knowledge, notes, projects, domain information, technical facts. Each topic should have a short descriptive slug (e.g. ""project-fait"", ""meeting-notes-may-2026"", ""recipe-ideas"").

Respond with ONLY valid JSON in this exact shape:
{
  ""soulMd"": ""<markdown string, or null if none>"",
  ""agentsMd"": ""<markdown string, or null if none>"",
  ""userMd"": ""<markdown string, or null if none>"",
  ""topics"": [{ ""topic"": ""<slug>"", ""content"": ""<markdown content>"" }]
}

If no content fits a category, set it to null (soulMd/agentsMd/userMd) or empty array (topics).
Do not include any text outside the JSON.";

        var invokeRequest = new InvokeModelRequest
        {
            ModelId = modelId,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 8192,
                system = systemPrompt,
                messages = new[] { new { role = "user", content = $"Classify this imported content:\n\n{stripped}" } }
            })))
        };

        var bedrockResponse = await _bedrock.InvokeModelAsync(invokeRequest, ct);
        var responseBody = await new StreamReader(bedrockResponse.Body).ReadToEndAsync(ct);

        ImportClassification routing;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var classifyText = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()?.Trim() ?? "";
            routing = JsonSerializer.Deserialize<ImportClassification>(classifyText)
                ?? new ImportClassification(null, null, null, []);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MemoryImport] Classification parse failed — falling back to single topic");
            routing = new ImportClassification(null, null, null,
                [new TopicEntry("imported-memory", stripped)]);
        }

        var dateHeader = $"\n\n## Imported from AI export — {DateTime.UtcNow:yyyy-MM-dd}\n\n";

        if (!string.IsNullOrWhiteSpace(routing.SoulMd))
        {
            var existing = await ReadAssistantFileAsync(userId, "SOUL.md", ct) ?? "";
            await WriteAssistantFileAsync(userId, "SOUL.md", existing.TrimEnd() + dateHeader + routing.SoulMd, ct);
            _logger.LogDebug("[MemoryImport] Updated SOUL.md for user {UserId}", userId);
        }

        if (!string.IsNullOrWhiteSpace(routing.AgentsMd))
        {
            var existing = await ReadAssistantFileAsync(userId, "AGENTS.md", ct) ?? "";
            await WriteAssistantFileAsync(userId, "AGENTS.md", existing.TrimEnd() + dateHeader + routing.AgentsMd, ct);
            _logger.LogDebug("[MemoryImport] Updated AGENTS.md for user {UserId}", userId);
        }

        if (!string.IsNullOrWhiteSpace(routing.UserMd))
        {
            var existing = await ReadAssistantFileAsync(userId, "USER.md", ct) ?? "";
            await WriteAssistantFileAsync(userId, "USER.md", existing.TrimEnd() + "\n\n" + routing.UserMd, ct);
            _logger.LogDebug("[MemoryImport] Updated USER.md for user {UserId}", userId);
        }

        int totalChunks = 0;
        foreach (var topic in routing.Topics ?? [])
        {
            if (string.IsNullOrWhiteSpace(topic.Topic) || string.IsNullOrWhiteSpace(topic.Content))
                continue;
            var title = char.ToUpper(topic.Topic[0]) + topic.Topic[1..].Replace("-", " ");
            await WriteTopicAsync(userId, topic.Topic, title, topic.Content, ct);

            // Count chunks using same sizing as harness (500 char, 50 overlap)
            const int chunkSize = 500, chunkOverlap = 50;
            for (int i = 0; i < topic.Content.Length; i += chunkSize - chunkOverlap)
            {
                totalChunks++;
                if (i + chunkSize >= topic.Content.Length) break;
            }
        }

        return new ImportMemoryResult(totalChunks);
    }

    private async Task<string?> ReadAssistantFileAsync(Guid userId, string filename, CancellationToken ct)
    {
        try
        {
            var resp = await _s3.GetObjectAsync(BucketName, AssistantKey(userId, filename), ct);
            using var reader = new StreamReader(resp.ResponseStream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return null;
        }
    }

    private async Task WriteAssistantFileAsync(Guid userId, string filename, string content, CancellationToken ct)
    {
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = AssistantKey(userId, filename),
            ContentBody = content,
            ContentType = "text/markdown"
        }, ct);
    }

    private record ImportClassification(
        [property: JsonPropertyName("soulMd")] string? SoulMd,
        [property: JsonPropertyName("agentsMd")] string? AgentsMd,
        [property: JsonPropertyName("userMd")] string? UserMd,
        [property: JsonPropertyName("topics")] List<TopicEntry>? Topics
    );

    private record TopicEntry(
        [property: JsonPropertyName("topic")] string Topic,
        [property: JsonPropertyName("content")] string Content
    );
}
