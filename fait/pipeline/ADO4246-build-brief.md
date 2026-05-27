# CC Brief: ADO#4246 — CloudFront Signed URLs for Office Online File Preview

## Context

FAIT's artifact sidebar (`ArtifactSidebarPanel.razor`) currently generates presigned S3 URLs to serve PPTX and XLSX files. When passed to Office Online embed (`https://view.officeapps.live.com/op/embed.aspx?src=<url>`), Microsoft's servers try to fetch the file and get a 403 — presigned S3 URLs are not publicly accessible from external servers.

**Solution:** Add an `ICloudFrontSignedUrlService` that generates short-lived CloudFront signed URLs (configurable, default 3600s). Update `WorkspaceFileService.GetPresignedDownloadUrlAsync` to use CloudFront signed URLs when CloudFront is configured. Update the sidebar to use Office Online inline iframe embed (instead of fallback "Open in Office Online" link) when a CloudFront signed URL is available.

---

## Files to Read First (understand before changing)

1. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/IWorkspaceFileService.cs`
2. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/WorkspaceFileService.cs`
3. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`
4. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs` (lines 90–145)
5. `/home/fredw/projects/fip/fait/src/FortressAI.Web/appsettings.json`
6. `/home/fredw/projects/fip/fait/src/FortressAI.Web/FortressAI.Web.csproj`

---

## Task 1: Add `AWSSDK.CloudFront` and `AWSSDK.SecretsManager` NuGet packages

In `/home/fredw/projects/fip/fait/src/FortressAI.Web/FortressAI.Web.csproj`, add inside the existing `<ItemGroup>` with other AWSSDK packages:

```xml
<PackageReference Include="AWSSDK.CloudFront" Version="3.7.*" />
<PackageReference Include="AWSSDK.SecretsManager" Version="3.7.*" />
```

---

## Task 2: Create `ICloudFrontSignedUrlService`

**File:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/ICloudFrontSignedUrlService.cs`

```csharp
namespace FortressAI.Web.Services;

/// <summary>
/// Generates short-lived CloudFront signed URLs for S3-backed objects.
/// Returns null when CloudFront is not configured (falls back to S3 presigned URLs).
/// </summary>
public interface ICloudFrontSignedUrlService
{
    /// <summary>
    /// Returns true when the service is configured and ready to sign URLs.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Generates a CloudFront signed URL for the given S3 key.
    /// </summary>
    /// <param name="s3Key">The S3 object key (e.g. "files/abc123/report.pptx")</param>
    /// <param name="expirySeconds">URL validity in seconds. Defaults to configured value (3600).</param>
    /// <returns>A signed CloudFront URL, or null if not configured.</returns>
    Task<string?> GetSignedUrlAsync(string s3Key, int? expirySeconds = null);
}
```

---

## Task 3: Create `CloudFrontSignedUrlService`

**File:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/CloudFrontSignedUrlService.cs`

**Design:**
- Reads config from `IConfiguration`: `CloudFront:DistributionDomain`, `CloudFront:KeyPairId`, `CloudFront:PrivateKeySecretName`, `CloudFront:UrlExpirySeconds` (default 3600)
- If `DistributionDomain` or `KeyPairId` or `PrivateKeySecretName` is missing/empty → `IsConfigured = false`, `GetSignedUrlAsync` returns `null`
- On first call, loads the RSA private key PEM from AWS Secrets Manager (by secret name) and caches it in-memory (singleton pattern via a lazy-loaded field)
- Signs the URL using `Amazon.CloudFront.AmazonCloudFrontUrlSigner` from `AWSSDK.CloudFront`
- Uses `AmazonCloudFrontUrlSigner.GetCannedSignedURL` with canned policy (simple, not custom)
- The signed URL format: `https://<DistributionDomain>/<s3Key>`

**Implementation sketch:**

```csharp
using Amazon.CloudFront;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using System.Security.Cryptography;

namespace FortressAI.Web.Services;

public class CloudFrontSignedUrlService : ICloudFrontSignedUrlService
{
    private readonly string? _distributionDomain;
    private readonly string? _keyPairId;
    private readonly string? _privateKeySecretName;
    private readonly int _urlExpirySeconds;
    private readonly IAmazonSecretsManager _secrets;
    private readonly ILogger<CloudFrontSignedUrlService> _logger;
    private RSA? _cachedPrivateKey;
    private readonly SemaphoreSlim _keyLoadLock = new(1, 1);

    public bool IsConfigured { get; }

    public CloudFrontSignedUrlService(
        IConfiguration config,
        IAmazonSecretsManager secrets,
        ILogger<CloudFrontSignedUrlService> logger)
    {
        _secrets = secrets;
        _logger = logger;
        _distributionDomain = config["CloudFront:DistributionDomain"];
        _keyPairId = config["CloudFront:KeyPairId"];
        _privateKeySecretName = config["CloudFront:PrivateKeySecretName"];
        _urlExpirySeconds = int.TryParse(config["CloudFront:UrlExpirySeconds"], out var s) ? s : 3600;
        IsConfigured = !string.IsNullOrEmpty(_distributionDomain)
                       && !string.IsNullOrEmpty(_keyPairId)
                       && !string.IsNullOrEmpty(_privateKeySecretName);
    }

    public async Task<string?> GetSignedUrlAsync(string s3Key, int? expirySeconds = null)
    {
        if (!IsConfigured) return null;
        var expiry = expirySeconds ?? _urlExpirySeconds;
        
        var privateKey = await GetPrivateKeyAsync();
        if (privateKey == null) return null;

        var resourceUrl = $"https://{_distributionDomain}/{s3Key}";
        var expiresAt = DateTime.UtcNow.AddSeconds(expiry);

        var signedUrl = AmazonCloudFrontUrlSigner.GetCannedSignedURL(
            AmazonCloudFrontUrlSigner.Protocol.https,
            _distributionDomain!,
            privateKey,
            s3Key,
            _keyPairId!,
            expiresAt);

        return signedUrl;
    }

    private async Task<TextReader?> GetPrivateKeyAsync()
    {
        // _cachedPrivateKey is a TextReader wrapping the PEM string
        // We need to reload from cache on each call since TextReader is consumed.
        // Better: cache the PEM string itself, create a new StringReader each call.
        // Implementation detail: cache the PEM string as a private field _cachedPem.
    }
}
```

**IMPORTANT — Correct implementation details:**

The `AmazonCloudFrontUrlSigner.GetCannedSignedURL` method signature is:
```csharp
public static string GetCannedSignedURL(
    Protocol protocol,
    string distributionDomain,
    TextReader privateKey,       // ← TextReader of PEM key
    string s3ObjectKey,
    string keyPairId,
    DateTime expiresOn)
```

So the approach is:
1. Cache the PEM string (`_cachedPem`) as a private `string?` field in the class
2. Load it once from Secrets Manager using a `SemaphoreSlim` lock
3. On each `GetSignedUrlAsync` call, wrap it in `new StringReader(_cachedPem)` to pass to the signer
4. Use `Protocol.https` enum value

**Full implementation:**

```csharp
using Amazon.CloudFront;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace FortressAI.Web.Services;

public class CloudFrontSignedUrlService : ICloudFrontSignedUrlService
{
    private readonly string? _distributionDomain;
    private readonly string? _keyPairId;
    private readonly string? _privateKeySecretName;
    private readonly int _urlExpirySeconds;
    private readonly IAmazonSecretsManager _secrets;
    private readonly ILogger<CloudFrontSignedUrlService> _logger;
    private string? _cachedPem;
    private readonly SemaphoreSlim _keyLoadLock = new(1, 1);

    public bool IsConfigured { get; }

    public CloudFrontSignedUrlService(
        IConfiguration config,
        IAmazonSecretsManager secrets,
        ILogger<CloudFrontSignedUrlService> logger)
    {
        _secrets = secrets;
        _logger = logger;
        _distributionDomain = config["CloudFront:DistributionDomain"];
        _keyPairId = config["CloudFront:KeyPairId"];
        _privateKeySecretName = config["CloudFront:PrivateKeySecretName"];
        _urlExpirySeconds = int.TryParse(config["CloudFront:UrlExpirySeconds"], out var s) ? s : 3600;
        IsConfigured = !string.IsNullOrEmpty(_distributionDomain)
                       && !string.IsNullOrEmpty(_keyPairId)
                       && !string.IsNullOrEmpty(_privateKeySecretName);
    }

    public async Task<string?> GetSignedUrlAsync(string s3Key, int? expirySeconds = null)
    {
        if (!IsConfigured) return null;
        
        var pem = await EnsurePemLoadedAsync();
        if (pem == null) return null;

        var expiry = expirySeconds ?? _urlExpirySeconds;
        var expiresAt = DateTime.UtcNow.AddSeconds(expiry);

        try
        {
            using var reader = new StringReader(pem);
            var signedUrl = AmazonCloudFrontUrlSigner.GetCannedSignedURL(
                AmazonCloudFrontUrlSigner.Protocol.https,
                _distributionDomain!,
                reader,
                s3Key,
                _keyPairId!,
                expiresAt);
            return signedUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate CloudFront signed URL for key {S3Key}", s3Key);
            return null;
        }
    }

    private async Task<string?> EnsurePemLoadedAsync()
    {
        if (_cachedPem != null) return _cachedPem;
        
        await _keyLoadLock.WaitAsync();
        try
        {
            if (_cachedPem != null) return _cachedPem; // double-check after lock
            
            var response = await _secrets.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = _privateKeySecretName
            });
            _cachedPem = response.SecretString;
            _logger.LogInformation("CloudFront private key loaded from Secrets Manager secret '{SecretName}'", _privateKeySecretName);
            return _cachedPem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load CloudFront private key from Secrets Manager secret '{SecretName}'", _privateKeySecretName);
            return null;
        }
        finally
        {
            _keyLoadLock.Release();
        }
    }
}
```

---

## Task 4: Update `IWorkspaceFileService`

**File:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/IWorkspaceFileService.cs`

Add a new method to the interface (keep the existing `GetPresignedDownloadUrlAsync`):

```csharp
/// <summary>
/// Returns a CloudFront signed URL if CloudFront is configured, otherwise falls back to S3 presigned URL.
/// Use this for Office Online embed (requires publicly accessible URL).
/// </summary>
Task<string> GetFilePreviewUrlAsync(string s3Key, int? expirySeconds = null, CancellationToken ct = default);
```

---

## Task 5: Update `WorkspaceFileService`

**File:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/WorkspaceFileService.cs`

1. Add `ICloudFrontSignedUrlService _cloudFront` constructor injection
2. Implement `GetFilePreviewUrlAsync`:
   - If `_cloudFront.IsConfigured`, call `_cloudFront.GetSignedUrlAsync(s3Key, expirySeconds)` — if result is non-null, return it
   - Otherwise fall back to `GetPresignedDownloadUrlAsync(s3Key, expiryMinutes: expirySeconds.HasValue ? expirySeconds.Value / 60 : 30, ct)`

**Updated constructor signature:**
```csharp
public WorkspaceFileService(
    IDbContextFactory<AppDbContext> dbFactory,
    IAmazonS3 s3,
    IConfiguration config,
    ICloudFrontSignedUrlService cloudFront,
    ILogger<WorkspaceFileService> logger)
```

---

## Task 6: Update `ArtifactSidebarPanel.razor`

**File:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`

**Changes:**

1. Inject `ICloudFrontSignedUrlService CloudFrontSvc` at the top with other `@inject` directives

2. In the `@using` section, ensure `FortressAI.Web.Services` is already there (it is).

3. In `SelectArtifact`, change the Office file handling branch. Currently:
```csharp
else
{
    // Office Online requires a publicly accessible URL.
    // Presigned S3 URLs are private and rejected by Microsoft's servers.
    // Store the rawUrl for the "Open in Office Online" link, set _previewUrl to null.
    _officeOnlineRawUrl = rawUrl;
    _previewUrl = null;
}
```

Replace with:
```csharp
else
{
    // Office Online requires a publicly accessible URL.
    // Use CloudFront signed URL (publicly resolvable) if available; otherwise store for fallback link.
    if (CloudFrontSvc.IsConfigured)
    {
        var signedUrl = await CloudFrontSvc.GetSignedUrlAsync(artifact.S3Key);
        if (signedUrl != null)
        {
            var encodedSrc = Uri.EscapeDataString(signedUrl);
            _previewUrl = $"https://view.officeapps.live.com/op/embed.aspx?src={encodedSrc}";
            _officeOnlineRawUrl = null;
        }
        else
        {
            _officeOnlineRawUrl = rawUrl;
            _previewUrl = null;
        }
    }
    else
    {
        _officeOnlineRawUrl = rawUrl;
        _previewUrl = null;
    }
}
```

4. The iframe `sandbox` attribute needs to allow Office Online to work properly. Update it:
```razor
<iframe src="@_previewUrl" class="artifact-sidebar__iframe"
        sandbox="allow-scripts allow-same-origin allow-popups allow-forms allow-popups-to-escape-sandbox" />
```

Note: `allow-popups-to-escape-sandbox` is needed for Office Online embeds to work correctly.

5. Add `.xlsx` to the `PreviewableExtensions` set:
```csharp
private static readonly HashSet<string> PreviewableExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx"
};
```

---

## Task 7: Register services in `Program.cs`

**File:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs`

1. Add SecretsManager client singleton (after the existing S3 singleton, around line 130):
```csharp
builder.Services.AddSingleton<Amazon.SecretsManager.IAmazonSecretsManager>(sp =>
    new Amazon.SecretsManager.AmazonSecretsManagerClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));
```

2. Add CloudFront service singleton (after SecretsManager):
```csharp
builder.Services.AddSingleton<ICloudFrontSignedUrlService, CloudFrontSignedUrlService>();
```

---

## Task 8: Update `appsettings.json`

**File:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/appsettings.json`

Add the CloudFront config section (with empty values — secrets are set via environment variables in ECS):

```json
"CloudFront": {
  "DistributionDomain": "",
  "KeyPairId": "",
  "PrivateKeySecretName": "",
  "UrlExpirySeconds": "3600"
}
```

Insert this before the closing `}` of the JSON file, after the last existing key (`ScheduledTasks`).

---

## Constraints

- Do NOT modify `GetPresignedDownloadUrlAsync` — it is still used for download (non-preview) operations and for text/PDF previews where the file is fetched server-side
- Do NOT touch any auth, KB, or unrelated services
- Do NOT modify `DatabaseInitializationService.cs` — no DB changes needed
- Do NOT add `.xlsx` to `TextPreviewableExtensions`
- The `CloudFrontSignedUrlService` must be registered as **Singleton** (PEM key cached, shared across requests)
- The `ICloudFrontSignedUrlService` injection in `WorkspaceFileService` means `WorkspaceFileService` constructor now takes `ICloudFrontSignedUrlService` — it's registered as Scoped and receives the Singleton via DI (fine)
- Keep all existing behavior for PDF, text, and download flows — no regressions

---

## Acceptance Criteria Check

After making changes, verify:
1. `ICloudFrontSignedUrlService` and `CloudFrontSignedUrlService` created
2. `WorkspaceFileService` constructor takes `ICloudFrontSignedUrlService`, implements `GetFilePreviewUrlAsync`
3. `IWorkspaceFileService` has `GetFilePreviewUrlAsync`
4. `ArtifactSidebarPanel.razor` uses CF signed URL → Office Online iframe when `IsConfigured`, else falls back to link
5. `.xlsx` added to `PreviewableExtensions`
6. `AWSSDK.CloudFront` and `AWSSDK.SecretsManager` in .csproj
7. Services registered in `Program.cs`
8. `appsettings.json` has `CloudFront` section with empty placeholder values

---

## Output

After making all changes, run:
```bash
cd /home/fredw/projects/fip/fait && dotnet build src/FortressAI.Web/FortressAI.Web.csproj 2>&1 | tail -20
```

Report build success or any errors. Do NOT run `dotnet publish`.

Then emit the completion message:
```
openclaw system event --text "ADO4246 CC build complete" --mode now
```
