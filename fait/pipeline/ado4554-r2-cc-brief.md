# CC Brief: ADO4554 R2 — Review Cycle 2 Fixes

## Context
Hawkeye reviewed the artifact proxy endpoint (ADO4554) and returned NEEDS-CHANGES. Apply the following fixes exactly as specified. No scope creep, no other changes.

---

## Fix C1 — ArtifactPreviewService.cs: Add startup guard for empty PREVIEW_TOKEN_SECRET

**File:** `src/FortressAI.Web/Services/ArtifactPreviewService.cs`

In the constructor, after the existing `_secret = config["PREVIEW_TOKEN_SECRET"] ?? "";` and `_logger = logger;` lines, add the guard:

```csharp
if (string.IsNullOrWhiteSpace(_secret))
    throw new InvalidOperationException(
        "PREVIEW_TOKEN_SECRET is not configured. This setting is required. " +
        "Set PREVIEW_TOKEN_SECRET in your ECS task definition or environment.");
```

The constructor should look like this after the fix:
```csharp
public ArtifactPreviewService(IConfiguration config, ILogger<ArtifactPreviewService> logger)
{
    _secret = config["PREVIEW_TOKEN_SECRET"] ?? "";
    _logger = logger;
    if (string.IsNullOrWhiteSpace(_secret))
        throw new InvalidOperationException(
            "PREVIEW_TOKEN_SECRET is not configured. This setting is required. " +
            "Set PREVIEW_TOKEN_SECRET in your ECS task definition or environment.");
}
```

---

## Fix I1 — ArtifactPreviewController.cs: Add expiry pre-check before DB lookup

**File:** `src/FortressAI.Web/Controllers/ArtifactPreviewController.cs`

At the top of the `Preview` action method, after the empty token check (`if (string.IsNullOrEmpty(token))`), add the expiry pre-check BEFORE the database access:

```csharp
// Fail immediately on expired token — before touching the DB
var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
if (now >= expires)
    return Unauthorized(new { error = "Invalid or expired token" });
```

This uses `>=` (not `>`) which also fixes the N1 off-by-one at the exact expiry boundary.

---

## Fix I2 — ArtifactPreviewController.cs: Fix Content-Disposition header encoding

**File:** `src/FortressAI.Web/Controllers/ArtifactPreviewController.cs`

Replace this line:
```csharp
Response.Headers["Content-Disposition"] = $"inline; filename=\"{artifact.Filename}\"";
```

With proper RFC 5987 encoding:
```csharp
var cd = new System.Net.Http.Headers.ContentDispositionHeaderValue("inline");
cd.FileNameStar = artifact.Filename;
Response.Headers["Content-Disposition"] = cd.ToString();
```

---

## Fix I3 — ArtifactPreviewController.cs: Guard against empty MimeType

**File:** `src/FortressAI.Web/Controllers/ArtifactPreviewController.cs`

Replace:
```csharp
Response.ContentType = artifact.MimeType;
```

With:
```csharp
Response.ContentType = !string.IsNullOrEmpty(artifact.MimeType)
    ? artifact.MimeType
    : "application/octet-stream";
```

---

## Fix I4 — ArtifactPreviewController.cs: Wrap CopyToAsync in try/catch with logging

**File:** `src/FortressAI.Web/Controllers/ArtifactPreviewController.cs`

The `CopyToAsync` call is currently outside the existing try/catch. Replace:
```csharp
        try
        {
            var s3Response = await _s3.GetObjectAsync(_bucket, artifact.S3Key);

            // Stream directly to response — no buffering
            Response.ContentType = artifact.MimeType;
            Response.ContentLength = artifact.SizeBytes > 0 ? artifact.SizeBytes : null;
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{artifact.Filename}\"";
            // Prevent caching of sensitive preview content
            Response.Headers["Cache-Control"] = "private, no-store";

            await s3Response.ResponseStream.CopyToAsync(Response.Body);
            return new EmptyResult();
        }
        catch (AmazonS3Exception s3Ex)
        {
            _logger.LogError(s3Ex, "[ArtifactPreview] S3 fetch failed for artifact {Id}, key={Key}, code={Code}",
                id, artifact.S3Key, s3Ex.ErrorCode);
            return StatusCode(502, new { error = "File unavailable" });
        }
```

With the updated version that:
1. Applies I2 fix (ContentDispositionHeaderValue)
2. Applies I3 fix (MimeType guard)
3. Wraps CopyToAsync in its own inner try/catch for logging:

```csharp
        try
        {
            var s3Response = await _s3.GetObjectAsync(_bucket, artifact.S3Key);

            // Stream directly to response — no buffering
            Response.ContentType = !string.IsNullOrEmpty(artifact.MimeType)
                ? artifact.MimeType
                : "application/octet-stream";
            Response.ContentLength = artifact.SizeBytes > 0 ? artifact.SizeBytes : null;
            var cd = new System.Net.Http.Headers.ContentDispositionHeaderValue("inline");
            cd.FileNameStar = artifact.Filename;
            Response.Headers["Content-Disposition"] = cd.ToString();
            // Prevent caching of sensitive preview content
            Response.Headers["Cache-Control"] = "private, no-store";

            try
            {
                await s3Response.ResponseStream.CopyToAsync(Response.Body);
            }
            catch (Exception copyEx)
            {
                _logger.LogError(copyEx, "[ArtifactPreview] Stream copy failed mid-response for artifact {Id}", id);
                // Cannot change status code — response already started
            }
            return new EmptyResult();
        }
        catch (AmazonS3Exception s3Ex)
        {
            _logger.LogError(s3Ex, "[ArtifactPreview] S3 fetch failed for artifact {Id}, key={Key}, code={Code}",
                id, artifact.S3Key, s3Ex.ErrorCode);
            return StatusCode(502, new { error = "File unavailable" });
        }
```

---

## Fix I5 — ArtifactPreviewPanel.razor: Remove allow-same-origin from iframe sandbox

**File:** `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor`

Find the iframe element with the sandbox attribute. Replace:
```html
sandbox="allow-scripts allow-same-origin allow-popups allow-forms"
```

With:
```html
sandbox="allow-scripts allow-popups allow-forms allow-popups-to-escape-sandbox"
```

---

## Fix N2 (optional) — Return null instead of "" on auth parse failure

**File:** `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor`

In the `GetProxyPreviewUrlAsync` method, change:
```csharp
if (!Guid.TryParse(userIdStr, out var userId)) return "";
```

To:
```csharp
if (!Guid.TryParse(userIdStr, out var userId)) return null;
```

Also update the method return type from `Task<string>` to `Task<string?>` since it can return null.

Then update the caller to handle null. In `LoadPreview()`, the line:
```csharp
_presignedUrl = await GetProxyPreviewUrlAsync(artifact);
```
Already assigns to `_presignedUrl` which is `string?` — so this is fine. The iframe only renders when `_presignedUrl != null` (the check `else if (_presignedUrl != null)` in the template handles this correctly).

---

## Important Notes

- Do NOT modify any other files
- Do NOT change any test files
- Do NOT change Program.cs or any other service registrations
- After applying all changes, verify the code compiles with `dotnet build`
- The fixes to I2, I3, and I4 all touch the same `try` block in `ArtifactPreviewController.cs` — apply them together as one coherent replacement (shown in the I4 fix block above)
