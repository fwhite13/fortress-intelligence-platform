# CC Brief: ADO4554 — Artifact Proxy Endpoint for File Delivery

## Objective
Implement a stateless HMAC-authenticated proxy endpoint that serves artifact file bytes to the browser for client-side rendering. This is the prerequisite for all Epic 12 renderer WIs.

## Project Location
`/home/fredw/projects/fip/fait/src/FortressAI.Web/`

---

## Task 1: Create ArtifactPreviewService

Create a new service at `src/FortressAI.Web/Services/ArtifactPreviewService.cs`.

This service provides:
1. Token generation: HMAC-SHA256 over `{artifactId}:{userId}:{expires}` using `PREVIEW_TOKEN_SECRET` env var
2. Token validation: verify signature + expiry

```csharp
using System.Security.Cryptography;
using System.Text;

namespace FortressAI.Web.Services;

/// <summary>
/// Provides HMAC-SHA256 token generation and validation for artifact preview URLs.
/// Token format: base64url(HMAC-SHA256("{artifactId}:{userId}:{expires}"))
/// where expires is a Unix timestamp (seconds).
/// Token validity: 15 minutes from generation.
/// PREVIEW_TOKEN_SECRET env var is the HMAC key.
/// </summary>
public class ArtifactPreviewService
{
    private readonly string _secret;
    private readonly ILogger<ArtifactPreviewService> _logger;
    private const int TokenValiditySeconds = 900; // 15 minutes

    public ArtifactPreviewService(IConfiguration config, ILogger<ArtifactPreviewService> logger)
    {
        _secret = config["PREVIEW_TOKEN_SECRET"] ?? "";
        _logger = logger;
    }

    /// <summary>
    /// Generates a preview token for the given artifact and user.
    /// Returns (token, expiresUnixTimestamp).
    /// </summary>
    public (string token, long expires) GenerateToken(Guid artifactId, Guid userId)
    {
        var expires = DateTimeOffset.UtcNow.AddSeconds(TokenValiditySeconds).ToUnixTimeSeconds();
        var payload = $"{artifactId}:{userId}:{expires}";
        var token = ComputeHmac(payload);
        return (token, expires);
    }

    /// <summary>
    /// Validates a preview token. Returns the artifactId if valid, null if invalid or expired.
    /// </summary>
    public bool ValidateToken(Guid artifactId, Guid userId, string token, long expires)
    {
        // Check expiry
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now > expires)
        {
            _logger.LogDebug("[ArtifactPreview] Token expired for artifact {ArtifactId}", artifactId);
            return false;
        }

        // Recompute HMAC and compare
        var payload = $"{artifactId}:{userId}:{expires}";
        var expected = ComputeHmac(payload);
        return CryptographicEquals(expected, token);
    }

    private string ComputeHmac(string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_secret);
        var msgBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(msgBytes);
        // Use base64url (no padding, URL-safe)
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool CryptographicEquals(string a, string b)
    {
        // Constant-time comparison to prevent timing attacks
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
```

Register in `Program.cs`:
- Add `builder.Services.AddScoped<ArtifactPreviewService>();` near the other scoped services (around line 117 where `IWorkspaceFileService` is registered).

---

## Task 2: Create ArtifactPreviewController

Create `src/FortressAI.Web/Controllers/ArtifactPreviewController.cs`.

This controller:
- Endpoint: `GET /api/artifacts/{id}/preview?token=<hmac>&expires=<unix-ts>`
- NO `[Authorize]` attribute — token IS the auth
- Validates HMAC token + expiry → 401 if invalid/expired
- Looks up the artifact in DB (WorkspaceUploads table) — returns 404 if not found
- Validates that the userId embedded in the token matches the artifact's owner UserId
- Fetches file from S3 using `IAmazonS3.GetObjectAsync` (same pattern as WorkspaceUploadService.cs line 379)
- Streams bytes to response with correct Content-Type header
- S3 bucket from config: `WORKSPACE_S3_BUCKET` env var, default `fortress-user-workspaces`

Pattern to follow for S3 fetch (from WorkspaceUploadService.cs):
```csharp
var response = await _s3.GetObjectAsync(_bucket, s3Key);
// stream response.ResponseStream directly to HTTP response
```

Full implementation:
```csharp
using Microsoft.AspNetCore.Mvc;
using Amazon.S3;
using FortressAI.Web.Services;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Controllers;

[ApiController]
[Route("api/artifacts")]
public class ArtifactPreviewController : ControllerBase
{
    private readonly ArtifactPreviewService _previewService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<ArtifactPreviewController> _logger;

    public ArtifactPreviewController(
        ArtifactPreviewService previewService,
        IDbContextFactory<AppDbContext> dbFactory,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<ArtifactPreviewController> logger)
    {
        _previewService = previewService;
        _dbFactory = dbFactory;
        _s3 = s3;
        _bucket = config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";
        _logger = logger;
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(
        Guid id,
        [FromQuery] string token,
        [FromQuery] long expires)
    {
        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { error = "Missing token" });

        // We need the userId to validate the token. We'll look up the artifact first,
        // then validate that the token's userId matches the artifact owner.
        // To do this without a 2-pass query, we extract userId from the artifact record
        // and validate it against the token.

        await using var db = await _dbFactory.CreateDbContextAsync();
        var artifact = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == id);

        if (artifact == null)
        {
            _logger.LogDebug("[ArtifactPreview] Artifact {Id} not found", id);
            return NotFound();
        }

        // Validate token against artifact's owner userId
        if (!_previewService.ValidateToken(id, artifact.UserId, token, expires))
        {
            _logger.LogWarning("[ArtifactPreview] Invalid or expired token for artifact {Id}", id);
            return Unauthorized(new { error = "Invalid or expired token" });
        }

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
        catch (Amazon.S3.AmazonS3Exception s3Ex)
        {
            _logger.LogError(s3Ex, "[ArtifactPreview] S3 fetch failed for artifact {Id}, key={Key}, code={Code}", 
                id, artifact.S3Key, s3Ex.ErrorCode);
            return StatusCode(502, new { error = "File unavailable" });
        }
    }
}
```

---

## Task 3: Add PREVIEW_TOKEN_SECRET to appsettings.json

In `src/FortressAI.Web/appsettings.json`, add a new top-level key:

```json
"PreviewToken": {
  "Secret": ""
}
```

Wait — the spec says load from env var `PREVIEW_TOKEN_SECRET`. Use `config["PREVIEW_TOKEN_SECRET"]` directly (no nested config section needed). The env var overrides appsettings automatically in ASP.NET Core.

Just add a comment placeholder in appsettings.json so operators know the variable exists. Add to appsettings.json (under the existing top-level keys):

```json
"PREVIEW_TOKEN_SECRET": ""
```

This goes in `appsettings.json` at the top level (alongside "Logging", "AllowedHosts", etc.).

---

## Task 4: Update ArtifactSidebarPanel.razor to use the proxy endpoint

In `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`:

The component currently injects `IWorkspaceFileService WorkspaceFileSvc` and uses `GetPresignedDownloadUrlAsync` to get S3 presigned URLs for preview/download.

We need to:
1. Inject `ArtifactPreviewService PreviewSvc`
2. Add a private method `GetProxyPreviewUrl(WorkspaceUpload artifact)` that:
   - Gets the current userId from Blazor's auth state
   - Calls `PreviewSvc.GenerateToken(artifact.Id, userId)` → `(token, expires)`
   - Returns `/api/artifacts/{artifact.Id}/preview?token={token}&expires={expires}`
3. Update `SelectArtifact` method:
   - For PDF files: use `GetProxyPreviewUrl` instead of `GetPresignedDownloadUrlAsync`
   - For text files: still fetch via presigned URL (text content is fetched server-side via HttpClient, not streamed to browser — this is fine)
   - For Office files: keep existing behavior (Office Online requires publicly accessible URL)
4. Update `DownloadAsync` method: keep using `GetPresignedDownloadUrlAsync` for downloads (the proxy endpoint serves inline, not as a download attachment)

The component needs:
- `@inject ArtifactPreviewService PreviewSvc`
- `@inject AuthenticationStateProvider AuthStateProvider` (to get current user)
- `@using Microsoft.AspNetCore.Components.Authorization`

Add a helper method:
```csharp
private async Task<string> GetProxyPreviewUrlAsync(WorkspaceUpload artifact)
{
    var authState = await AuthStateProvider.GetAuthenticationStateAsync();
    var userIdStr = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userIdStr, out var userId)) return "";
    var (token, expires) = PreviewSvc.GenerateToken(artifact.Id, userId);
    return $"/api/artifacts/{artifact.Id}/preview?token={Uri.EscapeDataString(token)}&expires={expires}";
}
```

In `SelectArtifact`, update the PDF branch (currently sets `_previewUrl = rawUrl`):
```csharp
else if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
{
    _previewUrl = await GetProxyPreviewUrlAsync(artifact);
}
```

Keep the download flow using presigned URLs as-is.

---

## Task 5: Update ArtifactRef to include artifact Id

In `src/FortressAI.Web/Services/ChatLayoutState.cs`, the `ArtifactRef` record is:
```csharp
public record ArtifactRef(string S3Key, string Filename, string MimeType);
```

Update it to include the artifact's Guid Id:
```csharp
public record ArtifactRef(Guid Id, string S3Key, string Filename, string MimeType);
```

Also update the one call site in `src/FortressAI.Web/Components/Chat/ChatView.razor` line 694:
```csharp
// Current:
LayoutState.OpenArtifactPreview(new ArtifactRef(artifact.S3Key, artifact.Filename, artifact.MimeType));
// Change to:
LayoutState.OpenArtifactPreview(new ArtifactRef(artifact.Id, artifact.S3Key, artifact.Filename, artifact.MimeType));
```

---

## Task 6: Update ArtifactPreviewPanel.razor to use the proxy endpoint

In `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor`:

Currently injects `IWorkspaceFileService WorkspaceFileSvc` and calls `GetPresignedDownloadUrlAsync`.

Make same changes:
1. Inject `ArtifactPreviewService PreviewSvc`
2. Inject `AuthenticationStateProvider AuthStateProvider`
3. Add `@using Microsoft.AspNetCore.Components.Authorization`
4. Add the same `GetProxyPreviewUrlAsync` helper method — but using `ArtifactRef`::
```csharp
private async Task<string> GetProxyPreviewUrlAsync(ArtifactRef artifact)
{
    var authState = await AuthStateProvider.GetAuthenticationStateAsync();
    var userIdStr = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userIdStr, out var userId)) return "";
    var (token, expires) = PreviewSvc.GenerateToken(artifact.Id, userId);
    return $"/api/artifacts/{artifact.Id}/preview?token={Uri.EscapeDataString(token)}&expires={expires}";
}
```
5. In `LoadPreview`, for PDF branch:
   ```csharp
   if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
   {
       _presignedUrl = await GetProxyPreviewUrlAsync(artifact);
   }
   ```
   The `_rawPresignedUrl` for download can still use `GetPresignedDownloadUrlAsync`.

---

## Summary of Files to Create/Modify

### CREATE:
- `src/FortressAI.Web/Services/ArtifactPreviewService.cs` — HMAC token service

### MODIFY:
- `src/FortressAI.Web/Controllers/ArtifactPreviewController.cs` — CREATE new file, proxy endpoint
- `src/FortressAI.Web/Program.cs` — Register `ArtifactPreviewService` as scoped
- `src/FortressAI.Web/appsettings.json` — Add `PREVIEW_TOKEN_SECRET` placeholder key
- `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor` — Use proxy URL for PDF preview
- `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor` — Use proxy URL for PDF preview
- `src/FortressAI.Web/Services/ChatLayoutState.cs` — Add `Id` to `ArtifactRef` record
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — Update `ArtifactRef` constructor call to include artifact Id

---

## Critical Constraints
1. NO `[Authorize]` on the new endpoint — token IS the auth
2. HMAC must use `PREVIEW_TOKEN_SECRET` env var loaded via `IConfiguration["PREVIEW_TOKEN_SECRET"]`
3. Token format: HMAC-SHA256 base64url of `{artifactId}:{userId}:{expires}`
4. Expiry: 15 minutes from generation
5. Invalid token → 401, Expired token → 401
6. S3 fetch pattern: `_s3.GetObjectAsync(_bucket, artifact.S3Key)` then stream `response.ResponseStream`
7. Do NOT use `edit`/`write` tools for code — this is the CC brief
8. After creating all files, run: `cd /home/fredw/projects/fip/fait && dotnet build src/FortressAI.Web/FortressAI.Web.csproj 2>&1 | tail -30`

---

## Output
After all changes are made and build succeeds, output a summary of:
1. Files created/modified
2. Build result
3. Any issues or deviations from this brief
