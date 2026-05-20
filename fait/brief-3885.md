# ADO#3885 — Avatar Moderation Hookup (Feature 10.3)

## File to Modify
`src/FortressAI.Web/Components/Pages/Settings.razor`

## What to Change

In `HandleAvatarUpload` (~line 706), the current code opens a stream directly from `file.OpenReadStream(...)` and passes it to `S3.PutObjectAsync`. We need to insert a content moderation check BEFORE the S3 upload.

### Current code (inside try block, after old-avatar delete):

```csharp
await using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
await S3.PutObjectAsync(new PutObjectRequest
{
    BucketName = bucket, Key = key,
    InputStream = stream, ContentType = file.ContentType
});
```

### Replace with:

```csharp
// Read into MemoryStream so we can use it for both moderation AND S3
await using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
var memStream = new MemoryStream();
await stream.CopyToAsync(memStream);
memStream.Position = 0;

// Content moderation check — fail-open (CheckImageAsync handles exceptions internally)
var moderationResult = await ContentModerationService.CheckImageAsync(memStream, file.ContentType);
if (!moderationResult.IsAllowed)
{
    _avatarError = "That image is not appropriate for a workplace app.";
    StateHasChanged();
    return;
}

// Reset stream position before S3 upload (critical — otherwise S3 gets 0 bytes)
memStream.Position = 0;
await S3.PutObjectAsync(new PutObjectRequest
{
    BucketName = bucket, Key = key,
    InputStream = memStream, ContentType = file.ContentType
});
```

## Constraints
- Do NOT add a new `@inject ContentModerationService ContentModerationService` — it's already on line 21
- Do NOT add a new `_avatarError` field — it already exists (~line 418)
- Do NOT add a UI display for `_avatarError` — it's already rendered at ~line 133
- Do NOT add any try/catch around CheckImageAsync — fail-open is handled inside the service
- No DB changes
- No other files need to change

## Acceptance Criteria
1. MemoryStream created by reading from `file.OpenReadStream`
2. `CheckImageAsync(memStream, file.ContentType)` called before `S3.PutObjectAsync`
3. On moderation fail: `_avatarError` set to "That image is not appropriate for a workplace app.", `StateHasChanged()` called, early return (no S3 upload)
4. `memStream.Position = 0` reset before `S3.PutObjectAsync`
5. Normal images upload to S3 as before
6. No extra try/catch added
7. `dotnet build` passes with 0 errors

## After Making the Change
Run:
```bash
cd /home/fredw/projects/fip/fait && dotnet build src/FortressAI.Web/FortressAI.Web.csproj 2>&1 | tail -20
```
Confirm 0 errors. If there are errors, fix them.
