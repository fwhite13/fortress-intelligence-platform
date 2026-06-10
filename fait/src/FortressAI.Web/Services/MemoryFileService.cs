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

        // Strip wrapper formats (code fences and banner lines) from input
        var stripped = content.Trim();
        if (stripped.StartsWith("```"))
        {
            var firstNewline = stripped.IndexOf('\n');
            var lastFence = stripped.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
                stripped = stripped[(firstNewline + 1)..lastFence].Trim();
        }
        stripped = Regex.Replace(stripped, @"^={5,}.*$", "", RegexOptions.Multiline).Trim();

        // Fetch all existing user memory files so Bedrock can merge with full context
        var existingFiles = new Dictionary<string, string>();

        foreach (var filename in new[] { "SOUL.md", "USER.md", "AGENTS.md" })
            existingFiles[$"assistants/{filename}"] = await ReadAssistantFileAsync(userId, filename, ct) ?? "";

        try
        {
            var idxResp = await _s3.GetObjectAsync(BucketName, IndexKey(userId), ct);
            using var idxReader = new StreamReader(idxResp.ResponseStream);
            existingFiles["memory/MEMORY.md"] = await idxReader.ReadToEndAsync(ct);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey") { existingFiles["memory/MEMORY.md"] = ""; }

        var topicListReq = new ListObjectsV2Request { BucketName = BucketName, Prefix = $"workspaces/{userId}/memory/" };
        ListObjectsV2Response topicListResp;
        do
        {
            topicListResp = await _s3.ListObjectsV2Async(topicListReq, ct);
            foreach (var obj in topicListResp.S3Objects)
            {
                if (!obj.Key.EndsWith(".md")) continue;
                var fname = obj.Key.Split('/').Last();
                if (fname.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var topicResp = await _s3.GetObjectAsync(BucketName, obj.Key, ct);
                    using var rdr = new StreamReader(topicResp.ResponseStream);
                    var slug = fname.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? fname[..^3] : fname;
                    existingFiles[$"memory/topics/{slug}.md"] = await rdr.ReadToEndAsync(ct);
                }
                catch (AmazonS3Exception ex) when (ex.ErrorCode is "NoSuchKey" or "AccessDenied")
                {
                    _logger.LogWarning("[MemoryImport] Skipping {Key}: {ErrorCode}", obj.Key, ex.ErrorCode);
                }
            }
            topicListReq.ContinuationToken = topicListResp.NextContinuationToken;
        } while (topicListResp.IsTruncated);

        // Build user message with all existing file context
        var msgSb = new StringBuilder();
        msgSb.AppendLine("## Existing memory files");
        msgSb.AppendLine();
        foreach (var (path, fileContent) in existingFiles)
        {
            msgSb.AppendLine($"### {path}");
            msgSb.AppendLine(string.IsNullOrWhiteSpace(fileContent) ? "(empty — file does not exist yet)" : fileContent);
            msgSb.AppendLine();
        }
        msgSb.AppendLine("## Imported content to merge");
        msgSb.AppendLine();
        msgSb.Append(stripped);

        var modelId = _config["BEDROCK_MODEL_ID"] ?? "us.anthropic.claude-sonnet-4-6";
        const string systemPrompt = @"You are a memory integration specialist. You will be given:
1. A set of existing memory files for a user's AI assistant
2. New content imported from another AI system

Your job is to intelligently merge the imported content into the existing files. Follow these rules:

- SOUL.md: Update with any behavioral guidance, tone preferences, communication style, or assistant persona instructions found in the imported content. Preserve all existing content — only add or refine.
- USER.md: Update with any personal facts, professional info, preferences, or identity information found in the imported content. Preserve all existing content — only add or refine.
- AGENTS.md: Update with any explicit rules, always/never instructions, behavioral constraints, or corrections found in the imported content. Preserve all existing content — only add or refine. Create this file if it doesn't exist and there is relevant content.
- MEMORY.md: Update the index if you create any new topic files. Preserve all existing index entries.
- Topic files: For content that doesn't belong in the above files (projects, domain knowledge, notes, etc.), either add it to the most appropriate existing topic file or create a new topic file. Use short descriptive slugs (e.g. projects.md, career.md, family.md). Only create a new topic file if no existing one is a good fit.

Return ONLY a JSON object in this exact shape — no other text, no code fences, no explanation:
{
  ""files"": [
    { ""path"": ""assistants/SOUL.md"", ""content"": ""<complete updated file content>"" },
    { ""path"": ""memory/topics/projects.md"", ""content"": ""<complete updated file content>"" }
  ]
}

Include ONLY files that have changed or been newly created. If a file does not need to change, omit it entirely. File content should be the complete updated content of the file, not a diff.";

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
                messages = new[] { new { role = "user", content = msgSb.ToString() } }
            })))
        };

        var bedrockResponse = await _bedrock.InvokeModelAsync(invokeRequest, ct);
        var responseBody = await new StreamReader(bedrockResponse.Body).ReadToEndAsync(ct);

        string classifyText;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            classifyText = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MemoryImport] Failed to parse Bedrock response envelope — falling back to single topic");
            await WriteTopicAsync(userId, "imported-memory", "Imported Memory", stripped, ct);
            return new ImportMemoryResult(1);
        }

        _logger.LogDebug("[MemoryImport] Raw Bedrock response: {Response}", classifyText);

        // Strip code fences from Bedrock response before JSON parse
        var parsableText = classifyText.Trim();
        if (parsableText.StartsWith("```"))
        {
            var firstNewline = parsableText.IndexOf('\n');
            var lastFence = parsableText.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
                parsableText = parsableText[(firstNewline + 1)..lastFence].Trim();
        }

        ImportMergeResponse mergeResponse;
        try
        {
            mergeResponse = JsonSerializer.Deserialize<ImportMergeResponse>(parsableText)
                ?? new ImportMergeResponse(null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MemoryImport] Classification parse failed — falling back to single topic");
            await WriteTopicAsync(userId, "imported-memory", "Imported Memory", stripped, ct);
            return new ImportMemoryResult(1);
        }

        int filesWritten = 0;
        foreach (var file in mergeResponse.Files ?? [])
        {
            if (string.IsNullOrWhiteSpace(file.Path) || string.IsNullOrWhiteSpace(file.Content))
                continue;

            _logger.LogInformation("[MemoryImport] Writing {Path} for user {UserId}", file.Path, userId);

            if (file.Path.StartsWith("assistants/"))
            {
                var filename = file.Path["assistants/".Length..];
                await WriteAssistantFileAsync(userId, filename, file.Content, ct);
                filesWritten++;
            }
            else if (file.Path.StartsWith("memory/topics/"))
            {
                var fname = file.Path["memory/topics/".Length..];
                var slug = fname.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? fname[..^3] : fname;
                if (string.IsNullOrWhiteSpace(slug)) continue;
                var title = System.Globalization.CultureInfo.InvariantCulture.TextInfo
                    .ToTitleCase(slug.Replace("-", " "));
                await WriteTopicAsync(userId, slug, title, file.Content, ct);
                filesWritten++;
            }
            // memory/MEMORY.md is rebuilt automatically by WriteTopicAsync — skip if returned
        }

        return new ImportMemoryResult(filesWritten);
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

    private record ImportMergeResponse(
        [property: JsonPropertyName("files")] List<ImportFileEntry>? Files
    );

    private record ImportFileEntry(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("content")] string Content
    );
}
