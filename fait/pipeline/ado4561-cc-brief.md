# CC Brief: ADO4561 — MemoryFileService ExportZipAsync S3 Fallback

## File to Modify
`/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/MemoryFileService.cs`

## Problem
`ExportZipAsync` calls `GetTopicsAsync` to get topics from the DB. If the user has no `memory_topics` rows, the returned zip is empty — even though their memory files exist in S3.

## Fix Required
In `ExportZipAsync`, after calling `GetTopicsAsync`, add an S3 fallback: if `topics.Count == 0`, list objects under `workspaces/{userId}/memory/` via `ListObjectsV2Async` and zip all `.md` files found there. If topics is non-empty, use the existing loop unchanged.

## Current ExportZipAsync (around line 160)
```csharp
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
```

## Required Change
Replace the `var topics = await GetTopicsAsync(userId, ct);` + `foreach` block with this logic:

```csharp
var topics = await GetTopicsAsync(userId, ct);

if (topics.Count == 0)
{
    // Fall back to S3 listing
    var listResp = await _s3.ListObjectsV2Async(new ListObjectsV2Request
    {
        BucketName = BucketName,
        Prefix = $"workspaces/{userId}/memory/"
    }, ct);
    
    foreach (var s3obj in listResp.S3Objects)
    {
        if (!s3obj.Key.EndsWith(".md")) continue;
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
        catch { /* skip unreadable files */ }
    }
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
```

## Variable Names (confirmed from reading the file)
- S3 client: `_s3` ✓
- Bucket name property: `BucketName` ✓
- ZipArchive local: `zip` ✓
- topics is `List<MemoryTopic>` from EF — `.Count == 0` is fine ✓
- `ListObjectsV2Async` is available on `IAmazonS3` ✓

## Constraints
- Do NOT touch any other methods
- Do NOT change the MEMORY.md index block (the try/catch after the topics block) — leave it exactly as-is
- The only change is replacing the `var topics = ...` + `foreach (var topic in topics)` block with the if/else pattern above
- Keep the rest of ExportZipAsync identical

## Acceptance Criteria
1. If `GetTopicsAsync` returns empty list → S3 fallback lists `workspaces/{userId}/memory/` and zips all `.md` files
2. Unreadable S3 files are skipped silently
3. Zip filenames are the last segment of the S3 key (e.g., `goals.md`)
4. If `GetTopicsAsync` returns non-empty list → existing topic loop runs unchanged
5. MEMORY.md index block remains unchanged after the topics section
6. No compilation errors

## Output
After making the change, run:
```
cd /home/fredw/projects/fip/fait && dotnet build src/FortressAI.Web/FortressAI.Web.csproj --no-incremental 2>&1 | tail -20
```
Report the build result.
