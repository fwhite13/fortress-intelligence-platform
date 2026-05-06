# BUILD Plan — ADO#2833
## KB Upload: Apply pre-processing pipeline to Personal/Team KB (PPTX→PDF, image BDA)

**WI:** ADO#2833 | FAIT
**Repo:** `/home/fredw/projects/fip/fait/`
**Risk:** medium (new service + NuGet package, touches upload hot path)

---

## Context & Audit Findings

**PPTX→PDF**: Already present in `KbDocumentService.UploadDocumentAsync` (Personal/Team path, lines 54-76). `UploadProjectDocumentAsync` does NOT have it — but Fred's scope is "all KB tiers" so add it there too.

**BDA image processing**: Does not exist anywhere in the codebase. Needs a new `BdaProcessingService` using `AWSSDK.BedrockDataAutomationRuntime`.

**BDA flow (async):**
1. Upload image to S3 (already happens in `UploadDocumentAsync`)
2. Invoke `InvokeDataAutomationAsync` with input S3 URI + output S3 prefix
3. Poll `GetDataAutomationStatus` until `SUCCESS` or `FAILED` (max ~60s, 5s intervals)
4. Read output JSON from S3 output location
5. Extract text content (OCR + visual description) from output
6. Write `.txt` sidecar to S3 at `{originalKey}-bda-text.txt`
7. On failure: log warning, proceed (non-fatal — image is still stored)

---

## Implementation

### 1. Add NuGet package

In `FortressAI.Web.csproj` AND `FortressAI.Shared.csproj` (if needed):
```xml
<PackageReference Include="AWSSDK.BedrockDataAutomationRuntime" Version="4.0.*" />
```

Only add to `FortressAI.Web.csproj` — the service lives in Web.

### 2. New service: `BdaProcessingService.cs`

Location: `src/FortressAI.Web/Services/BdaProcessingService.cs`

```csharp
using Amazon.BedrockDataAutomationRuntime;
using Amazon.BedrockDataAutomationRuntime.Model;
using Amazon.S3;
using Amazon.S3.Model;

public class BdaProcessingService
{
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<BdaProcessingService> _logger;

    // BDA runtime client — us-east-1 only (BDA availability)
    private AmazonBedrockDataAutomationRuntimeClient CreateBdaClient() =>
        new AmazonBedrockDataAutomationRuntimeClient(Amazon.RegionEndpoint.USEast1);

    private string BucketName => _config["AWS:KnowledgeBaseBucket"] ?? "fortress-tools";
    
    // BDA profile ARN — use AWS-managed standard profile
    private const string BdaProfileArn = "arn:aws:bedrock:us-east-1::data-automation/standard-output-profile";

    public BdaProcessingService(IAmazonS3 s3, IConfiguration config, ILogger<BdaProcessingService> logger)
    {
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Run BDA processing on an image already uploaded to S3.
    /// Writes OCR+description text as a .txt sidecar at {s3Key}-bda-text.txt.
    /// Non-fatal: logs warning on failure, returns false.
    /// </summary>
    public async Task<bool> ProcessImageAsync(string s3Key, CancellationToken ct = default)
    {
        try
        {
            var inputUri = $"s3://{BucketName}/{s3Key}";
            var outputPrefix = $"bda-output/{s3Key}/";
            var outputUri = $"s3://{BucketName}/{outputPrefix}";

            _logger.LogInformation("[BDA] Starting image processing for {S3Key}", s3Key);

            using var bdaClient = CreateBdaClient();

            // Invoke BDA async job
            var invokeReq = new InvokeDataAutomationAsyncRequest
            {
                InputConfiguration = new InputDataConfiguration { S3Uri = inputUri },
                OutputConfiguration = new OutputDataConfiguration { S3Uri = outputUri },
                DataAutomationProfileArn = BdaProfileArn,
                ClientToken = Guid.NewGuid().ToString()
            };

            var invokeResp = await bdaClient.InvokeDataAutomationAsyncAsync(invokeReq, ct);
            var invocationArn = invokeResp.InvocationArn;

            _logger.LogInformation("[BDA] Job started, invocationArn={Arn}", invocationArn);

            // Poll for completion (max 12 × 5s = 60s)
            string? status = null;
            for (int i = 0; i < 12; i++)
            {
                await Task.Delay(5000, ct);
                var statusResp = await bdaClient.GetDataAutomationStatusAsync(
                    new GetDataAutomationStatusRequest { InvocationArn = invocationArn }, ct);
                status = statusResp.Status?.Value;
                _logger.LogInformation("[BDA] Poll {Attempt}: status={Status}", i + 1, status);
                if (status == "Success" || status == "ServiceError" || status == "ClientError") break;
            }

            if (status != "Success")
            {
                _logger.LogWarning("[BDA] Job did not succeed for {S3Key}: status={Status}", s3Key, status);
                return false;
            }

            // Read output JSON from S3
            // BDA writes a result JSON; find it under the output prefix
            var listResp = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = BucketName,
                Prefix = outputPrefix
            }, ct);

            var resultKey = listResp.S3Objects
                .Where(o => o.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.LastModified)
                .FirstOrDefault()?.Key;

            if (resultKey == null)
            {
                _logger.LogWarning("[BDA] No output JSON found for {S3Key}", s3Key);
                return false;
            }

            var getResp = await _s3.GetObjectAsync(new GetObjectRequest
            {
                BucketName = BucketName, Key = resultKey
            }, ct);

            using var reader = new StreamReader(getResp.ResponseStream);
            var resultJson = await reader.ReadToEndAsync(ct);

            // Extract text — BDA standard output JSON has "content" or "text" fields
            var extractedText = ExtractTextFromBdaOutput(resultJson, s3Key);
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                _logger.LogWarning("[BDA] No text extracted from output for {S3Key}", s3Key);
                return false;
            }

            // Write .txt sidecar
            var sidecarKey = $"{s3Key}-bda-text.txt";
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = sidecarKey,
                ContentBody = extractedText,
                ContentType = "text/plain"
            }, ct);

            _logger.LogInformation("[BDA] Sidecar written: {SidecarKey} ({Chars} chars)", sidecarKey, extractedText.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BDA] Image processing failed for {S3Key} — non-fatal, proceeding without BDA text", s3Key);
            return false;
        }
    }

    private static string ExtractTextFromBdaOutput(string json, string s3Key)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var sb = new System.Text.StringBuilder();

            // BDA standard output structure — walk common text fields
            // Try: root["output_segments"][*]["standard_output"]["text"]
            // Try: root["content"]
            // Try: root["text"]
            if (doc.RootElement.TryGetProperty("output_segments", out var segments))
            {
                foreach (var seg in segments.EnumerateArray())
                {
                    if (seg.TryGetProperty("standard_output", out var stdOut))
                    {
                        if (stdOut.TryGetProperty("text", out var textEl))
                            sb.AppendLine(textEl.GetString());
                        if (stdOut.TryGetProperty("semantic_modality_output", out var modal))
                        {
                            // images: description field
                            if (modal.TryGetProperty("description", out var desc))
                                sb.AppendLine(desc.GetString());
                        }
                    }
                }
            }
            else if (doc.RootElement.TryGetProperty("content", out var content))
            {
                sb.AppendLine(content.GetString());
            }
            else if (doc.RootElement.TryGetProperty("text", out var text))
            {
                sb.AppendLine(text.GetString());
            }

            return sb.ToString().Trim();
        }
        catch
        {
            return "";
        }
    }
}
```

**Note on BDA output schema:** The exact JSON structure may differ from what's shown. Tony should check the AWS BDA docs or SDK response model to get the real field names. The `ExtractTextFromBdaOutput` method should be adapted to whatever the SDK actually returns. A safe fallback: serialize the entire output JSON as text if specific fields aren't found.

**Note on `dataAutomationProfileArn`:** The standard AWS-managed profile ARN may differ. Tony should check:
- AWS docs: https://docs.aws.amazon.com/bedrock/latest/userguide/bda-using-api.html
- Or use `arn:aws:bedrock:us-east-1::data-automation/aws-standard-output-profile/1.0.0` (check actual ARN format)
- Alternatively, omit the profile ARN and see if BDA defaults to standard output

### 3. Register `BdaProcessingService` in `Program.cs`

```csharp
builder.Services.AddScoped<BdaProcessingService>();
```

### 4. Update `KbDocumentService.UploadDocumentAsync` — add image BDA processing

After PPTX conversion block (existing) and before S3 key construction, add:

```csharp
// Image BDA — process images via Bedrock Data Automation for OCR + visual description
private static readonly HashSet<string> BdaSupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

// In UploadDocumentAsync, after PPTX conversion block:
// (Note: BDA processing happens AFTER S3 upload, since BDA needs S3 URI as input)
```

The BDA processing should happen **after** S3 upload (since BDA needs the file in S3):

```csharp
// After _s3.PutObjectAsync for the main file:
var ext = Path.GetExtension(safeFilename).ToLowerInvariant();
if (BdaSupportedImageExtensions.Contains(ext))
{
    _logger.LogInformation("[KbDocumentService] Image detected — running BDA processing: {Key}", key);
    await _bdaService.ProcessImageAsync(key);
    // Non-fatal: BdaProcessingService.ProcessImageAsync catches all exceptions internally
}
```

Add `BdaProcessingService` to `KbDocumentService` constructor injection.

### 5. Update `KbDocumentService.UploadProjectDocumentAsync` — add PPTX→PDF + BDA

Same pattern as Personal/Team path:

**PPTX→PDF** (add at top of method, same as `UploadDocumentAsync`):
```csharp
// Auto-convert PPTX — same as UploadDocumentAsync
Stream uploadStream = fileStream;
if (safeFilename.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
{
    var pdfBytes = await ConvertPptxToPdfAsync(fileStream, safeFilename, _logger);
    if (pdfBytes != null)
    {
        uploadStream = new MemoryStream(pdfBytes);
        safeFilename = Path.ChangeExtension(safeFilename, ".pdf");
        contentType = "application/pdf";
    }
}
```

**BDA after S3 upload** (same as Personal/Team):
```csharp
var ext = Path.GetExtension(safeFilename).ToLowerInvariant();
if (BdaSupportedImageExtensions.Contains(ext))
    await _bdaService.ProcessImageAsync(key);
```

### 6. Update `KnowledgeBaseManagement.razor` help text

Find the supported formats text and add image types:
```
Supported: PDF, DOCX, TXT, MD, PPTX, JPG, PNG, GIF, WEBP · Max 10 MB (images: 3.75 MB) · PPTX auto-converted to PDF · Images indexed via OCR + visual analysis · Ingestion takes 1–5 minutes
```

---

## IAM Note

`BdaProcessingService` needs the ECS task role to have:
- `bedrock:InvokeDataAutomationAsync` (or similar BDA runtime action)
- `s3:PutObject` on the BDA output prefix (`bda-output/*`)
- `s3:GetObject` + `s3:ListObjectsV2` on same

The existing FAIT task role likely covers S3 already. BDA-specific permissions may need adding — Tony should note this in the Build Report so Rhodey can add if needed.

---

## Acceptance Criteria

- [ ] `BdaProcessingService` exists and is registered in DI
- [ ] `AWSSDK.BedrockDataAutomationRuntime` added to `.csproj`
- [ ] `UploadDocumentAsync` (Personal/Team): images trigger BDA → `.txt` sidecar written to S3 after upload
- [ ] `UploadProjectDocumentAsync` (Project): PPTX→PDF added; images trigger BDA
- [ ] BDA processing is non-fatal — upload succeeds even if BDA fails
- [ ] `KnowledgeBaseManagement.razor` help text updated to mention images + size limit
- [ ] Build compiles with 0 errors

---

## Files to create/modify

- `src/FortressAI.Web/Services/BdaProcessingService.cs` — new
- `src/FortressAI.Web/FortressAI.Web.csproj` — add NuGet package
- `src/FortressAI.Web/Program.cs` — register BdaProcessingService
- `src/FortressAI.Web/Services/KbDocumentService.cs` — inject BdaProcessingService, add image BDA + project PPTX
- `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor` — help text

---

## CC env vars
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
```
