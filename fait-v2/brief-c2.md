# CC Task Brief — ADO#2847 Build Cycle 2
# Fix MemoryFileService.cs — 5 specific issues only

## Target file (ONLY file to modify)
`/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Services/MemoryFileService.cs`

## DO NOT touch any other files. No new files. No reformatting of unchanged code.

---

## Fix I1 — Wrap inner GetObjectAsync calls with per-file error handling

### In `GetTopicsAsync` (around line 152-160):
Currently there is a bare `GetObjectAsync` call inside the foreach loop with NO try/catch.
Wrap it so each per-file fetch is isolated:

```csharp
// REPLACE this block:
var getResponse = await _s3.GetObjectAsync(new GetObjectRequest
{
    BucketName = _bucket,
    Key = obj.Key
}, ct);
using var reader = new StreamReader(getResponse.ResponseStream);
var content = await reader.ReadToEndAsync(ct);

topics.Add(new MemoryTopicEntry(slug, content, obj.LastModified));

// WITH this block:
try
{
    var getResponse = await _s3.GetObjectAsync(new GetObjectRequest
    {
        BucketName = _bucket,
        Key = obj.Key
    }, ct);
    using var reader = new StreamReader(getResponse.ResponseStream);
    var content = await reader.ReadToEndAsync(ct);
    topics.Add(new MemoryTopicEntry(slug, content, obj.LastModified));
}
catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey" || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    _logger.LogWarning("Topic key {Key} listed but not found — skipping", obj.Key);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error fetching topic key {Key} — skipping", obj.Key);
}
```

### In `ExportZipAsync` (around line 253-261):
Currently there is a bare `GetObjectAsync` call inside the foreach loop with NO try/catch.
Wrap it similarly:

```csharp
// REPLACE this block:
var getResponse = await _s3.GetObjectAsync(new GetObjectRequest
{
    BucketName = _bucket,
    Key = obj.Key
}, ct);

using var ms = new MemoryStream();
await getResponse.ResponseStream.CopyToAsync(ms, ct);
files.Add((relPath, ms.ToArray()));

// WITH this block:
try
{
    var getResponse = await _s3.GetObjectAsync(new GetObjectRequest
    {
        BucketName = _bucket,
        Key = obj.Key
    }, ct);
    using var ms = new MemoryStream();
    await getResponse.ResponseStream.CopyToAsync(ms, ct);
    files.Add((relPath, ms.ToArray()));
}
catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey" || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    _logger.LogWarning("Export: key {Key} listed but not found — skipping", obj.Key);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Export: unexpected error fetching key {Key} — skipping", obj.Key);
}
```

---

## Fix I2 — Remove dead 404 catch blocks from Delete methods

### In `DeleteFileAsync`:
Currently:
```csharp
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
```

Replace with:
```csharp
// S3 DeleteObject is idempotent — returns 204 whether or not the key exists; no 404 to catch
await _s3.DeleteObjectAsync(new DeleteObjectRequest
{
    BucketName = _bucket,
    Key = key
}, ct);
_logger.LogInformation("Deleted memory file {Key}", key);
```

### In `DeleteTopicAsync`:
Currently:
```csharp
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
```

Replace with:
```csharp
// S3 DeleteObject is idempotent — returns 204 whether or not the key exists; no 404 to catch
await _s3.DeleteObjectAsync(new DeleteObjectRequest
{
    BucketName = _bucket,
    Key = key
}, ct);
_logger.LogInformation("Deleted topic {Slug} for user {UserId}", topicSlug, userId);
```

---

## Fix I3 + I4 — Add input validation via a private helper + call it

Add this private static helper method to the class (place it right after the existing helpers section, before the S3 file operations region):

```csharp
/// <summary>
/// Validates that an id-style string (userId or topicSlug) contains only safe characters.
/// Prevents path traversal attacks in S3 key construction.
/// </summary>
private static void ValidateId(string value, string paramName)
{
    if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains(".."))
        throw new ArgumentException($"Invalid {paramName}: value is empty or contains disallowed characters.", paramName);
}
```

Then add calls at the top of each public method as follows:

- `ReadFileAsync`: add `ValidateId(userId, nameof(userId));` before `var key = FileKey(...)`
- `WriteFileAsync`: add `ValidateId(userId, nameof(userId));` before `var key = FileKey(...)`
- `DeleteFileAsync`: add `ValidateId(userId, nameof(userId));` before `var key = FileKey(...)`
- `ListFilesAsync`: add `ValidateId(userId, nameof(userId));` before `var prefix = MemoryPrefix(...)`
- `GetTopicsAsync`: add `ValidateId(userId, nameof(userId));` before `var prefix = TopicsPrefix(...)`
- `GetTopicAsync`: add `ValidateId(userId, nameof(userId));` then `ValidateId(topicSlug, nameof(topicSlug));` before `var key = TopicKey(...)`
- `UpsertTopicAsync`: add `ValidateId(userId, nameof(userId));` then `ValidateId(topicSlug, nameof(topicSlug));` before `var key = TopicKey(...)`
- `DeleteTopicAsync`: add `ValidateId(userId, nameof(userId));` then `ValidateId(topicSlug, nameof(topicSlug));` before `var key = TopicKey(...)`
- `ExportZipAsync`: add `ValidateId(userId, nameof(userId));` before `var prefix = MemoryPrefix(...)`

---

## Fix I6 — Add RemoveFromVectorIndexAsync stub in DeleteTopicAsync

After the S3 delete call in `DeleteTopicAsync` (after the logger line), add:
```csharp
// TODO: remove from pgvector index when PostgresMemoryService is wired (Sprint 2 follow-up)
await RemoveFromVectorIndexAsync(userId, topicSlug, ct);
```

Add the stub method alongside `SyncToVectorIndexAsync` at the bottom of the file (in the pgvector stub region):

```csharp
/// <summary>
/// Removes a topic from the pgvector index.
/// TODO: wire pgvector delete via PostgresMemoryService (Sprint 2 #2847 follow-up)
/// </summary>
private static Task RemoveFromVectorIndexAsync(string userId, string topicSlug, CancellationToken ct)
{
    // No-op stub — pgvector removal is Sprint 2
    return Task.CompletedTask;
}
```

---

## After making all changes:
1. Verify the file compiles — run: `cd /home/fredw/projects/fip/fait-v2 && dotnet build 2>&1 | tail -20`
2. Report exactly what lines were changed for each fix
3. Do NOT commit — Tony will commit after reviewing your output
