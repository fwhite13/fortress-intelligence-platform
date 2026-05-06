# CC Brief — ADO#2833: BdaProcessingService + Image BDA + PPTX Parity

You are implementing Bedrock Data Automation (BDA) image processing for the FAIT Knowledge Base upload pipeline.
Work from `/home/fredw/projects/fip/fait/`.

---

## STEP 1 — Add NuGet package

Run this exact command (do not use --no-restore, let it restore):
```bash
cd /home/fredw/projects/fip/fait/src/FortressAI.Web
dotnet add package AWSSDK.BedrockDataAutomationRuntime --version "3.7.*"
```

After the add, verify the package reference is in the .csproj. The version pattern `3.7.*` matches all existing AWSSDK packages in the project.

---

## STEP 2 — Create `BdaProcessingService.cs`

**Create:** `src/FortressAI.Web/Services/BdaProcessingService.cs`

```csharp
using Amazon.BedrockDataAutomationRuntime;
using Amazon.BedrockDataAutomationRuntime.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FortressAI.Web.Services;

/// <summary>
/// Runs Bedrock Data Automation (BDA) async processing on images already uploaded to S3.
/// Writes OCR + visual description as a .txt sidecar at {s3Key}-bda-text.txt.
/// All operations are non-fatal — logs warning on any failure.
/// </summary>
public class BdaProcessingService
{
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<BdaProcessingService> _logger;

    private const string BucketName = "fortress-tools";

    // BDA Runtime client — us-east-1 (BDA only available in us-east-1)
    private AmazonBedrockDataAutomationRuntimeClient CreateBdaClient() =>
        new AmazonBedrockDataAutomationRuntimeClient(Amazon.RegionEndpoint.USEast1);

    // AWS-managed standard output profile ARN for BDA
    // See: https://docs.aws.amazon.com/bedrock/latest/userguide/bda-using-api.html
    // NOTE: This ARN is OMITTED intentionally — BDA defaults to standard output when not specified.
    // If the SDK requires it, the format is: arn:aws:bedrock:us-east-1::data-automation/aws-standard-output-profile/1.0.0
    // We pass null/omit DataAutomationProfileArn to let BDA use its default.

    public BdaProcessingService(IAmazonS3 s3, IConfiguration config, ILogger<BdaProcessingService> logger)
    {
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Run BDA async processing on an image already in S3 at {s3Key}.
    /// On success: writes {s3Key}-bda-text.txt containing extracted OCR + description text.
    /// On failure: logs warning and returns false. Never throws.
    /// </summary>
    public async Task<bool> ProcessImageAsync(string s3Key, CancellationToken ct = default)
    {
        try
        {
            var inputUri  = $"s3://{BucketName}/{s3Key}";
            var outputPrefix = $"bda-output/{s3Key}/";
            var outputUri = $"s3://{BucketName}/{outputPrefix}";

            _logger.LogInformation("[BDA] Starting image processing for {S3Key}", s3Key);

            using var bdaClient = CreateBdaClient();

            // Invoke BDA async job
            var invokeReq = new InvokeDataAutomationAsyncRequest
            {
                InputConfiguration  = new InputDataConfiguration  { S3Uri = inputUri  },
                OutputConfiguration = new OutputDataConfiguration { S3Uri = outputUri },
                ClientToken = Guid.NewGuid().ToString("N")
                // DataAutomationProfileArn intentionally omitted — BDA uses standard default
            };

            var invokeResp = await bdaClient.InvokeDataAutomationAsyncAsync(invokeReq, ct);
            var invocationArn = invokeResp.InvocationArn;
            _logger.LogInformation("[BDA] Job submitted. InvocationArn={Arn}", invocationArn);

            // Poll for completion: up to 12 × 5 s = 60 s
            string? statusValue = null;
            for (int attempt = 1; attempt <= 12; attempt++)
            {
                await Task.Delay(5_000, ct);

                var statusResp = await bdaClient.GetDataAutomationStatusAsync(
                    new GetDataAutomationStatusRequest { InvocationArn = invocationArn }, ct);

                // AutomationJobStatus enum — .Value gives the string representation
                statusValue = statusResp.Status?.Value;
                _logger.LogInformation("[BDA] Poll {Attempt}/12: Status={Status}", attempt, statusValue);

                if (statusValue is "Success" or "ServiceError" or "ClientError")
                    break;
            }

            if (statusValue != "Success")
            {
                _logger.LogWarning("[BDA] Job did not succeed for {S3Key}: FinalStatus={Status}", s3Key, statusValue);
                return false;
            }

            // Find the output JSON under the output prefix
            var listResp = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = BucketName,
                Prefix     = outputPrefix
            }, ct);

            var resultKey = listResp.S3Objects
                .Where(o => o.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.LastModified)
                .FirstOrDefault()?.Key;

            if (resultKey == null)
            {
                _logger.LogWarning("[BDA] No output JSON found under prefix {Prefix}", outputPrefix);
                return false;
            }

            // Read output JSON
            var getResp = await _s3.GetObjectAsync(
                new GetObjectRequest { BucketName = BucketName, Key = resultKey }, ct);

            string resultJson;
            using (var reader = new StreamReader(getResp.ResponseStream))
                resultJson = await reader.ReadToEndAsync(ct);

            // Extract text from BDA output
            var extractedText = ExtractTextFromBdaOutput(resultJson);
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                _logger.LogWarning("[BDA] No text extracted from output JSON for {S3Key}", s3Key);
                return false;
            }

            // Write .txt sidecar
            var sidecarKey = $"{s3Key}-bda-text.txt";
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName  = BucketName,
                Key         = sidecarKey,
                ContentBody = extractedText,
                ContentType = "text/plain"
            }, ct);

            _logger.LogInformation("[BDA] Sidecar written: {SidecarKey} ({Chars} chars)", sidecarKey, extractedText.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BDA] Image processing failed non-fatally for {S3Key}", s3Key);
            return false;
        }
    }

    /// <summary>
    /// Extract text from the BDA standard output JSON.
    /// BDA output structure (standard profile):
    ///   root["output_segments"][i]["standard_output"]["text"]            — OCR text
    ///   root["output_segments"][i]["standard_output"]["semantic_modality_output"]["description"] — visual description
    /// Fallback: root["content"] or root["text"] for simpler response shapes.
    /// If none match, serializes the entire JSON as plain text so something is always stored.
    /// </summary>
    private static string ExtractTextFromBdaOutput(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var sb = new System.Text.StringBuilder();

            if (doc.RootElement.TryGetProperty("output_segments", out var segments))
            {
                foreach (var seg in segments.EnumerateArray())
                {
                    if (!seg.TryGetProperty("standard_output", out var stdOut)) continue;

                    if (stdOut.TryGetProperty("text", out var textEl) &&
                        textEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        sb.AppendLine(textEl.GetString());

                    if (stdOut.TryGetProperty("semantic_modality_output", out var modal) &&
                        modal.TryGetProperty("description", out var desc) &&
                        desc.ValueKind == System.Text.Json.JsonValueKind.String)
                        sb.AppendLine(desc.GetString());
                }
            }
            else if (doc.RootElement.TryGetProperty("content", out var content) &&
                     content.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                sb.AppendLine(content.GetString());
            }
            else if (doc.RootElement.TryGetProperty("text", out var text) &&
                     text.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                sb.AppendLine(text.GetString());
            }
            else
            {
                // Fallback: store the raw JSON so the sidecar file is never empty
                sb.AppendLine(json);
            }

            return sb.ToString().Trim();
        }
        catch
        {
            return json; // worst case: store raw JSON
        }
    }
}
```

---

## STEP 3 — Register `BdaProcessingService` in `Program.cs`

**File:** `src/FortressAI.Web/Program.cs`

Find the block:
```csharp
builder.Services.AddScoped<KbDocumentService>();
builder.Services.AddSingleton<KbSyncRetryService>();
```

Add `BdaProcessingService` registration **before** `KbDocumentService` (so it's available when KbDocumentService constructor resolves):

```csharp
builder.Services.AddScoped<BdaProcessingService>();
builder.Services.AddScoped<KbDocumentService>();
builder.Services.AddSingleton<KbSyncRetryService>();
```

---

## STEP 4 — Update `KbDocumentService.cs`

**File:** `src/FortressAI.Web/Services/KbDocumentService.cs`

### 4a — Add `BdaProcessingService` field + constructor injection

Current constructor signature:
```csharp
public KbDocumentService(IAmazonS3 s3, IAmazonBedrockAgent bedrockAgent, IConfiguration config, ILogger<KbDocumentService> logger, KbSyncRetryService syncRetryService, IDbContextFactory<AppDbContext> dbContextFactory)
{
    _s3 = s3;
    _bedrockAgent = bedrockAgent;
    _config = config;
    _logger = logger;
    _syncRetryService = syncRetryService;
    _dbContextFactory = dbContextFactory;
}
```

Add `BdaProcessingService _bdaService` field and update constructor. Add the private static HashSet for BDA-supported image extensions. The fields section currently has:

```csharp
private readonly IAmazonS3 _s3;
private readonly IAmazonBedrockAgent _bedrockAgent;
private readonly IConfiguration _config;
private readonly ILogger<KbDocumentService> _logger;
private readonly KbSyncRetryService _syncRetryService;
private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

private const string BucketName = "fortress-tools";
```

Replace with:
```csharp
private readonly IAmazonS3 _s3;
private readonly IAmazonBedrockAgent _bedrockAgent;
private readonly IConfiguration _config;
private readonly ILogger<KbDocumentService> _logger;
private readonly KbSyncRetryService _syncRetryService;
private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
private readonly BdaProcessingService _bdaService;

private const string BucketName = "fortress-tools";

/// <summary>Image extensions supported by Bedrock Data Automation for OCR + visual indexing.</summary>
private static readonly HashSet<string> BdaSupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
```

Replace constructor with:
```csharp
public KbDocumentService(IAmazonS3 s3, IAmazonBedrockAgent bedrockAgent, IConfiguration config, ILogger<KbDocumentService> logger, KbSyncRetryService syncRetryService, IDbContextFactory<AppDbContext> dbContextFactory, BdaProcessingService bdaService)
{
    _s3 = s3;
    _bedrockAgent = bedrockAgent;
    _config = config;
    _logger = logger;
    _syncRetryService = syncRetryService;
    _dbContextFactory = dbContextFactory;
    _bdaService = bdaService;
}
```

### 4b — Add BDA trigger to `UploadDocumentAsync` AFTER the S3 PutObjectAsync

In `UploadDocumentAsync`, after this block:
```csharp
await _s3.PutObjectAsync(putReq);
_logger.LogInformation("Uploaded KB document to s3://{Bucket}/{Key}", BucketName, key);
```

Add:
```csharp
// BDA image processing — runs AFTER S3 upload (BDA needs the file in S3 as input)
// Non-fatal: BdaProcessingService.ProcessImageAsync catches all exceptions internally
var fileExt = Path.GetExtension(safeFilename);
if (BdaSupportedImageExtensions.Contains(fileExt))
{
    _logger.LogInformation("[KbDocumentService] Image detected, invoking BDA processing: {Key}", key);
    _ = Task.Run(() => _bdaService.ProcessImageAsync(key), CancellationToken.None);
}
```

**Important:** Use `Task.Run` (fire-and-forget) so BDA processing doesn't block the upload response. BDA can take 5-60s; the upload should return immediately.

### 4c — Update `UploadProjectDocumentAsync` — add PPTX→PDF + BDA

Current `UploadProjectDocumentAsync` starts at:
```csharp
public async Task<string> UploadProjectDocumentAsync(Stream fileStream, string filename, string contentType, Guid projectId, Guid userId)
{
    var safeFilename = Path.GetFileName(filename);
    if (string.IsNullOrEmpty(safeFilename))
        throw new ArgumentException("Invalid filename.", nameof(filename));

    var key = $"kb-docs/project/{projectId}/{safeFilename}";

    await _s3.PutObjectAsync(new PutObjectRequest
    {
        BucketName = BucketName,
        Key = key,
        InputStream = fileStream,
        ContentType = contentType
    });

    _logger.LogInformation("Uploaded project KB document to s3://{Bucket}/{Key}", BucketName, key);
```

Replace the entire method body from `var safeFilename` up to and including the `_logger.LogInformation("Uploaded project KB document...)` line with:

```csharp
    var safeFilename = Path.GetFileName(filename);
    if (string.IsNullOrEmpty(safeFilename))
        throw new ArgumentException("Invalid filename.", nameof(filename));

    // Auto-convert PPTX — same as UploadDocumentAsync (Bedrock KB does not support .pptx natively)
    Stream uploadStream = fileStream;
    if (safeFilename.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
    {
        _logger.LogInformation("PPTX detected (project) — converting to PDF via LibreOffice: {Filename}", safeFilename);
        var pdfBytes = await ConvertPptxToPdfAsync(fileStream, safeFilename, _logger);

        if (pdfBytes != null)
        {
            var convertedFilename = Path.ChangeExtension(safeFilename, ".pdf");
            uploadStream = new MemoryStream(pdfBytes);
            safeFilename = convertedFilename;
            contentType = "application/pdf";
            _logger.LogInformation("PPTX converted to PDF (project): {Filename} ({Bytes} bytes)", convertedFilename, pdfBytes.Length);
        }
        else
        {
            _logger.LogWarning("PPTX→PDF conversion failed (project) — uploading original PPTX");
        }
    }

    var key = $"kb-docs/project/{projectId}/{safeFilename}";

    await _s3.PutObjectAsync(new PutObjectRequest
    {
        BucketName = BucketName,
        Key        = key,
        InputStream = uploadStream,
        ContentType = contentType
    });

    _logger.LogInformation("Uploaded project KB document to s3://{Bucket}/{Key}", BucketName, key);

    // BDA image processing — runs AFTER S3 upload (BDA needs the file in S3 as input)
    var projFileExt = Path.GetExtension(safeFilename);
    if (BdaSupportedImageExtensions.Contains(projFileExt))
    {
        _logger.LogInformation("[KbDocumentService] Image detected (project), invoking BDA processing: {Key}", key);
        _ = Task.Run(() => _bdaService.ProcessImageAsync(key), CancellationToken.None);
    }
```

(Leave the rest of the method — metadata write and return — unchanged.)

---

## STEP 5 — Update help text in `KnowledgeBaseManagement.razor`

**File:** `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor`

Find (exact text on line 89):
```
Supported: PDF, DOCX, TXT, MD, PPTX · Max 10 MB · PPTX auto-converted to PDF · Ingestion takes 1–5 minutes
```

Replace with:
```
Supported: PDF, DOCX, TXT, MD, PPTX, JPG, PNG, GIF, WEBP · Max 10 MB (images: 3.75 MB) · PPTX auto-converted to PDF · Images indexed via OCR + visual analysis · Ingestion takes 1–5 minutes
```

---

## STEP 6 — Build verification

Run:
```bash
cd /home/fredw/projects/fip/fait/src/FortressAI.Web && dotnet build --no-incremental 2>&1
```

The build MUST succeed with 0 errors before proceeding. If there are compile errors, fix them.

Common issues to watch for:
- `InvokeDataAutomationAsyncRequest` — verify the property names `InputConfiguration`, `OutputConfiguration` match the SDK. If they don't, check `Amazon.BedrockDataAutomationRuntime.Model` namespace for the real class.
- `AutomationJobStatus` enum — if `statusResp.Status?.Value` doesn't compile, check the actual response property name in the SDK. It may be `statusResp.Status.ToString()` or similar.
- If `InputDataConfiguration` / `OutputDataConfiguration` don't exist, check the actual model class names in the 3.7.x SDK.

**If SDK model class names differ from what's in the brief:** Adapt the code to use the actual class names from the SDK. Do not leave compile errors. Check by looking at:
```bash
find ~/.nuget/packages/awssdk.bedrockdataautomationruntime -name "*.cs" 2>/dev/null | head -5
ls ~/.nuget/packages/awssdk.bedrockdataautomationruntime/*/lib/net8.0/ 2>/dev/null
```

Then use a decompiler approach:
```bash
# Check what types are available in the assembly
dotnet-script -e "using System.Reflection; var a = Assembly.LoadFrom(\"$(find ~/.nuget/packages/awssdk.bedrockdataautomationruntime -name '*.dll' | head -1)\"); foreach (var t in a.GetTypes().Where(t => t.Name.Contains(\"DataAutomation\"))) Console.WriteLine(t.FullName);" 2>/dev/null || true
```

Or simply look at the .dll with a strings command:
```bash
strings $(find ~/.nuget/packages/awssdk.bedrockdataautomationruntime -name '*.dll' | grep net8 | head -1) 2>/dev/null | grep -i "dataautomation\|invocation\|inputconfig\|outputconfig" | head -30
```

---

## STEP 7 — Git commit

After successful build:
```bash
cd /home/fredw/projects/fip/fait
bash /home/fredw/.openclaw/workspace/scripts/preflight/git-commit.sh
git add src/FortressAI.Web/Services/BdaProcessingService.cs \
        src/FortressAI.Web/Services/KbDocumentService.cs \
        src/FortressAI.Web/Program.cs \
        src/FortressAI.Web/FortressAI.Web.csproj \
        src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor
git commit -m "feat(ADO#2833): BdaProcessingService + image BDA + PPTX parity for all KB tiers"
```

Capture the commit hash:
```bash
git log --oneline -1
```

---

## Summary of Files to Create/Modify

| File | Action |
|------|--------|
| `src/FortressAI.Web/Services/BdaProcessingService.cs` | **CREATE** |
| `src/FortressAI.Web/FortressAI.Web.csproj` | **MODIFY** — add NuGet ref |
| `src/FortressAI.Web/Program.cs` | **MODIFY** — register BdaProcessingService |
| `src/FortressAI.Web/Services/KbDocumentService.cs` | **MODIFY** — inject BdaProcessingService, add image BDA + project PPTX |
| `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor` | **MODIFY** — help text |

---

## Output

After the commit, print the following exactly:
```
COMMIT_HASH: <hash>
NUGET_VERSION: <resolved version of AWSSDK.BedrockDataAutomationRuntime>
FILES_CHANGED: BdaProcessingService.cs, KbDocumentService.cs, Program.cs, FortressAI.Web.csproj, KnowledgeBaseManagement.razor
BDA_PROFILE_ARN: OMITTED (DataAutomationProfileArn not set — BDA uses standard default)
BUILD_RESULT: SUCCEEDED
```
