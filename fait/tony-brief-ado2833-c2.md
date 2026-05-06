# CC Brief — ADO#2833 Cycle 2 (CORRECTIVE REVERT)

## Context
In cycle 1 (commit a0ff695), BdaProcessingService was built to handle image processing via Bedrock Data Automation Runtime.
Fred has clarified: BDA for images is handled by `ParsingStrategy: BEDROCK_NATIVE` on the KB data source config — NO application code needed.
This is a corrective build. Revert BdaProcessingService and all related code, but KEEP the PPTX→PDF additions.

## Repo root: /home/fredw/projects/fip/fait

---

## TASK 1: DELETE BdaProcessingService.cs

Delete this file entirely:
`src/FortressAI.Web/Services/BdaProcessingService.cs`

Use Bash to delete: `rm src/FortressAI.Web/Services/BdaProcessingService.cs`

---

## TASK 2: Edit FortressAI.Web.csproj

File: `src/FortressAI.Web/FortressAI.Web.csproj`

Remove this line exactly:
```
    <PackageReference Include="AWSSDK.BedrockDataAutomationRuntime" Version="3.7.*" />
```

---

## TASK 3: Edit Program.cs

File: `src/FortressAI.Web/Program.cs`

Remove this exact line (line 123):
```
builder.Services.AddScoped<BdaProcessingService>();
```

---

## TASK 4: Edit KbDocumentService.cs

File: `src/FortressAI.Web/Services/KbDocumentService.cs`

### Remove these items:

1. **Field declaration** — remove this line:
```csharp
    private readonly BdaProcessingService _bdaService;
```

2. **Static set** — remove these lines:
```csharp
    /// <summary>Image extensions supported by Bedrock Data Automation for OCR + visual indexing.</summary>
    private static readonly HashSet<string> BdaSupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
```

3. **Constructor parameter** — in the constructor signature, remove `, BdaProcessingService bdaService` parameter AND the assignment line `_bdaService = bdaService;`

The constructor currently ends with:
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

Change it to:
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

4. **BDA call site in UploadDocumentAsync** — remove these lines (after the S3 upload in UploadDocumentAsync):
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

5. **BDA call site in UploadProjectDocumentAsync** — remove these lines (after the S3 upload in UploadProjectDocumentAsync):
```csharp
        // BDA image processing — runs AFTER S3 upload (BDA needs the file in S3 as input)
        var projFileExt = Path.GetExtension(safeFilename);
        if (BdaSupportedImageExtensions.Contains(projFileExt))
        {
            _logger.LogInformation("[KbDocumentService] Image detected (project), invoking BDA processing: {Key}", key);
            _ = Task.Run(() => _bdaService.ProcessImageAsync(key), CancellationToken.None);
        }
```

### KEEP (DO NOT REMOVE):
- The entire PPTX→PDF conversion block in `UploadDocumentAsync` (ConvertPptxToPdfAsync call)
- The entire PPTX→PDF conversion block in `UploadProjectDocumentAsync` (ConvertPptxToPdfAsync call)
- The `ConvertPptxToPdfAsync` static method at the bottom of the class
- All other existing code

---

## TASK 5: Edit KnowledgeBaseManagement.razor

File: `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor`

Find the help text caption in the My KB tab that currently reads:
```
                        Supported: PDF, DOCX, TXT, MD, PPTX, JPG, PNG, GIF, WEBP · Max 10 MB (images: 3.75 MB) · PPTX auto-converted to PDF · Images indexed via OCR + visual analysis · Ingestion takes 1–5 minutes
```

Replace it with:
```
                        Supported: PDF, DOCX, TXT, MD, PPTX, JPG, PNG, GIF, WEBP · Max 10 MB · PPTX auto-converted to PDF · Ingestion takes 1–5 minutes
```

(Remove "images: 3.75 MB" size limit callout and "Images indexed via OCR + visual analysis" — image handling is transparent via Bedrock native parsing; no user-visible BDA note needed.)

---

## Verification steps after edits

Run: `cd src/FortressAI.Web && dotnet build`

It must compile with 0 errors. If there are errors, fix them.

Confirm:
- `BdaProcessingService.cs` does not exist
- No reference to `BdaProcessingService` anywhere in the codebase
- No reference to `AWSSDK.BedrockDataAutomationRuntime` in .csproj
- PPTX→PDF blocks remain intact in both UploadDocumentAsync and UploadProjectDocumentAsync
- Build succeeds

---

## Output
When done, print a summary of all files changed and confirm build succeeded.
