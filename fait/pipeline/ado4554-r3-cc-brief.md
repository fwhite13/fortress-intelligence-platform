# CC Brief: ADO4554 Review Cycle 3 — Two Targeted Fixes

## Context
Two small security/robustness fixes identified in Cycle 3 review. No other changes.

---

## Fix 1 — `ArtifactPreviewController.cs`

**File:** `src/FortressAI.Web/Controllers/ArtifactPreviewController.cs`

**Task:** Add a general `catch (Exception ex)` block immediately after the existing `catch (AmazonS3Exception s3Ex)` block.

The existing catch block is:
```csharp
catch (AmazonS3Exception s3Ex)
{
    _logger.LogError(s3Ex, "[ArtifactPreview] S3 fetch failed for artifact {Id}, key={Key}, code={Code}",
        id, artifact.S3Key, s3Ex.ErrorCode);
    return StatusCode(502, new { error = "File unavailable" });
}
```

Add this directly after it (before the closing brace of the outer try/catch structure):
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "[ArtifactPreview] Unexpected error fetching artifact {Id}", id);
    return StatusCode(500, new { error = "Internal error" });
}
```

**Important:** The inner `try/catch` around `CopyToAsync` (which catches `Exception copyEx`) is a separate nested block inside the outer `try` — do NOT touch it. Only add the new catch to the outer try/catch that wraps `GetObjectAsync`.

The outer try structure should end up looking like:
```csharp
try
{
    var s3Response = await _s3.GetObjectAsync(_bucket, artifact.S3Key);
    // ... headers/content-type setup ...
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
catch (Exception ex)
{
    _logger.LogError(ex, "[ArtifactPreview] Unexpected error fetching artifact {Id}", id);
    return StatusCode(500, new { error = "Internal error" });
}
```

---

## Fix 2 — `ArtifactPreviewPanel.razor`

**File:** `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor`

**Task:** Remove `allow-popups-to-escape-sandbox` from the iframe sandbox attribute.

Find this line:
```razor
sandbox="allow-scripts allow-popups allow-forms allow-popups-to-escape-sandbox"
```

Replace with:
```razor
sandbox="allow-scripts allow-popups allow-forms"
```

`allow-popups` alone keeps popups sandboxed (correct for user-uploaded content). `allow-popups-to-escape-sandbox` combined with `allow-scripts` creates a sandbox escape vector for PDFs with embedded JS — remove it.

---

## Constraints
- NO other changes to any files
- Exactly these 2 edits only
- Do not reformat, reorganize, or change anything else in either file
