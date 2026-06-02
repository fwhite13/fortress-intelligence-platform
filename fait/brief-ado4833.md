# CC Task: ADO#4833 — PPTX Preview Shows "Authentication error. Please refresh."

## Working directory
`/home/fredw/projects/fip/fait/`

## Files to modify
- `src/FortressAI.Web/Components/Chat/PptxPreviewPanel.razor`
- `src/FortressAI.Web/Controllers/ArtifactPreviewController.cs` (possible)

---

## Root Cause Analysis

**Root Cause 1 (Primary):** `PptxPreviewPanel.razor` uses `AuthenticationStateProvider` to extract a `Guid` userId from `ClaimTypes.NameIdentifier`. In production (Cognito), the `NameIdentifier` claim is a Cognito user UUID string (not the same as `AppUser.Id`). `Guid.TryParse` may succeed but returns the wrong ID — OR the Cognito sub format doesn't parse as Guid at all — causing `_error = "Authentication error. Please refresh."`.

The app uses `UserSessionService` (injected as `Session`) which holds the correct internal `AppUser.Id`. PptxPreviewPanel must use this instead.

**Root Cause 2 (Secondary):** `PptxPreviewPanel` calls `HttpClientFactory.CreateClient()` (anonymous client) to POST to `/api/artifacts/{id}/convert-pptx` which has `[Authorize]`. This call comes from Blazor Server's server-side render, not the browser — no auth cookie is sent, so it gets a 401/redirect which is not `OK` or `Accepted`, triggering the error path.

Since `PptxPreviewPanel` is a Blazor Server component, it should call the service directly instead of making an HTTP round-trip.

---

## Fix

### Step 1: Inject UserSessionService and ArtifactPreviewController dependencies directly

In `PptxPreviewPanel.razor`:

**Add injections:**
```razor
@inject FortressAI.Web.Services.UserSessionService Session
@inject FortressAI.Web.Data.IDbContextFactory<FortressAI.Web.Data.AppDbContext> DbFactory
@inject Microsoft.Extensions.Configuration.IConfiguration Config
@inject Amazon.S3.IAmazonS3 S3Client
```

Wait — injecting S3 directly may be overkill. Better approach: inject a new minimal service method OR refactor the conversion call.

### Better approach: Move conversion logic to ArtifactPreviewService

**In `ArtifactPreviewService.cs`**, add a method:
```csharp
/// <summary>
/// Triggers PPTX → PDF conversion via converter service.
/// Returns the preview S3 key if conversion succeeded, null otherwise.
/// </summary>
public async Task<string?> ConvertPptxAsync(Guid artifactId, string s3Key, Guid userId, IHttpClientFactory httpClientFactory)
{
    // Check DB first for cached key
    await using var db = await _dbFactory.CreateDbContextAsync();
    var upload = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == artifactId && u.UserId == userId);
    if (upload != null && !string.IsNullOrEmpty(upload.PreviewS3Key))
        return upload.PreviewS3Key;

    var converterBase = _config["CONVERTER_BASE_URL"] ?? "http://localhost:3001";
    var converterApiKey = _config["CONVERTER_API_KEY"];
    using var client = httpClientFactory.CreateClient("HarnessClient");
    if (!string.IsNullOrEmpty(converterApiKey))
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", converterApiKey);

    var body = new
    {
        artifactId = artifactId.ToString(),
        s3Key = s3Key,
        userId = userId.ToString(),
        outputBucket = _config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces"
    };
    var resp = await client.PostAsJsonAsync($"{converterBase}/convert", body);
    if (!resp.IsSuccessStatusCode)
    {
        _logger.LogWarning("[ArtifactPreview] PPTX converter returned {Status} for artifact {Id}", resp.StatusCode, artifactId);
        return null;
    }
    var result = await resp.Content.ReadFromJsonAsync<ConvertPptxResult>();
    if (result?.PreviewS3Key != null && upload != null)
    {
        upload.PreviewS3Key = result.PreviewS3Key;
        await db.SaveChangesAsync();
    }
    return result?.PreviewS3Key;
}

private record ConvertPptxResult([property: System.Text.Json.Serialization.JsonPropertyName("previewS3Key")] string? PreviewS3Key);
```

Note: `ArtifactPreviewService` already has `_config` (inject IConfiguration into constructor if not already there) and `_dbFactory`. Check existing constructor and add `IConfiguration config` parameter if missing and store as `_config`. Also inject `IHttpClientFactory` OR just accept it as a parameter (parameter is simpler since the service is already scoped).

Check existing ArtifactPreviewService constructor:
```csharp
public ArtifactPreviewService(IConfiguration config, ILogger<ArtifactPreviewService> logger, IDbContextFactory<AppDbContext> dbFactory)
{
    _secret = config["PREVIEW_TOKEN_SECRET"] ?? "";
```
Good — `IConfiguration` is already there. Store it:
```csharp
private readonly IConfiguration _config;
// Add in constructor:
_config = config;
```

### Step 2: Refactor PptxPreviewPanel to use Session.UserId and direct service call

Replace the entire `FetchAndConvertAsync()` method with a version that:
1. Gets userId from `Session.UserId` (NOT from `AuthStateProvider` claims)
2. Calls `PreviewSvc.ConvertPptxAsync(...)` directly (NO HTTP round-trip)
3. Generates HMAC token and fetches PDF bytes from the preview endpoint (unauthenticated HMAC endpoint is fine)

New `FetchAndConvertAsync()`:
```csharp
private async Task FetchAndConvertAsync()
{
    try
    {
        var userId = Session.UserId;
        if (userId == Guid.Empty)
        {
            _error = "Authentication error. Please refresh.";
            _loading = false;
            StateHasChanged();
            return;
        }

        // Step 1: Get or create preview key
        string? previewS3Key = ExistingPreviewS3Key;
        if (string.IsNullOrEmpty(previewS3Key))
        {
            // Call conversion service directly (no HTTP auth round-trip)
            previewS3Key = await PreviewSvc.ConvertPptxAsync(ArtifactId, S3Key, userId, HttpClientFactory);
        }

        if (string.IsNullOrEmpty(previewS3Key))
        {
            _error = "Conversion produced no output. Try downloading the file.";
            _loading = false;
            StateHasChanged();
            return;
        }

        // Step 2: Generate HMAC token and fetch PDF bytes from unauthenticated preview endpoint
        var (token, expires) = PreviewSvc.GenerateToken(ArtifactId, userId);
        var previewUrl = NavManager.ToAbsoluteUri(
            $"/api/artifacts/{ArtifactId}/preview?token={Uri.EscapeDataString(token)}&expires={expires}&preview=true"
        ).ToString();

        using var http = HttpClientFactory.CreateClient();
        _pendingBytes = await http.GetByteArrayAsync(previewUrl);
        _loading = false;
        StateHasChanged();
    }
    catch (Exception ex)
    {
        // Log exception details for diagnostics
        Console.Error.WriteLine($"[PptxPreview] FetchAndConvertAsync failed: {ex.Message}");
        _error = "Failed to load presentation preview. Try downloading the file.";
        _loading = false;
        StateHasChanged();
    }
}
```

### Step 3: Remove AuthenticationStateProvider injection from PptxPreviewPanel

Since `Session.UserId` replaces the claims lookup, `@inject AuthenticationStateProvider AuthStateProvider` is no longer needed. Remove it.

Also remove the polling code — `ConvertPptxAsync` is synchronous from the component's perspective (it awaits completion). The Accepted/polling path was needed for the HTTP round-trip; the direct service call is synchronous. Remove `_pollCts`, `DisposeAsync`, and the `PreviewStatusResponse` record if only used in polling.

Actually — preserve `IAsyncDisposable` and `DisposeAsync` to be safe but remove `_pollCts` since it's not used. Remove the `PreviewStatusResponse` record.

### Step 4: Add @inject UserSessionService to PptxPreviewPanel

```razor
@inject FortressAI.Web.Services.UserSessionService Session
```

---

## Acceptance Criteria
- AC1: PPTX files open in the side panel without "Authentication error. Please refresh."
- AC2: The conversion logic no longer goes through the `[Authorize]` HTTP endpoint from Blazor
- AC3: Preview renders PDF pages correctly
- AC4: Build compiles with 0 errors

---

## Final steps
1. Verify build: `cd /home/fredw/projects/fip/fait/src/FortressAI.Web && dotnet build --no-restore 2>&1 | tail -10`
2. Fix any compilation errors
3. Commit: `cd /home/fredw/projects/fip && git add -A && git commit -m "ADO#4833: PPTX preview auth error — use Session.UserId instead of claims, call ConvertPptxAsync directly bypassing [Authorize] HTTP round-trip"`
