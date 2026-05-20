# CC Brief: ADO#3824 Cycle 2 — ContentModerationService DI Fixes

## Context
Fix 4 code review issues in ContentModerationService. These are the ONLY changes — no scope creep.

---

## File 1: `src/FortressAI.Web/Services/ContentModerationService.cs`

### Current state (entire file):
```csharp
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FortressAI.Web.Services;

public record ModerationResult(bool Passed, string? Reason);

public class ContentModerationService
{
    private readonly AmazonBedrockRuntimeClient _bedrockClient;
    private readonly AmazonRekognitionClient _rekognitionClient;
    private readonly ILogger<ContentModerationService> _logger;

    private const string HaikuModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
    private const float ConfidenceThreshold = 70f;

    public ContentModerationService(ILogger<ContentModerationService> logger)
    {
        _logger = logger;
        _bedrockClient = new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.USEast1
        });
        _rekognitionClient = new AmazonRekognitionClient(new AmazonRekognitionConfig
        {
            RegionEndpoint = RegionEndpoint.USEast1
        });
    }
    // ... rest of class
}
```

### Required changes to ContentModerationService.cs:

1. **Change field types** from concrete to interface types:
   - `private readonly AmazonBedrockRuntimeClient _bedrockClient;` → `private readonly IAmazonBedrockRuntime _bedrockClient;`
   - `private readonly AmazonRekognitionClient _rekognitionClient;` → `private readonly IAmazonRekognition _rekognitionClient;`

2. **Remove** `private const string HaikuModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";`
   **Add** `private readonly string _haikuModelId;` field
   **Keep** `private const float ConfidenceThreshold = 70f;`

3. **Replace constructor** entirely with:
```csharp
public ContentModerationService(
    IAmazonBedrockRuntime bedrockClient,
    IAmazonRekognition rekognitionClient,
    IConfiguration configuration,
    ILogger<ContentModerationService> logger)
{
    _bedrockClient = bedrockClient;
    _rekognitionClient = rekognitionClient;
    _logger = logger;
    _haikuModelId = configuration["Bedrock:ModerationModelId"]
        ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0";
}
```

4. **Replace all uses of `HaikuModelId`** (the const) with `_haikuModelId` (the instance field). There is one use: `ModelId = HaikuModelId` in CheckNameAsync — change to `ModelId = _haikuModelId`.

5. **Add `using Microsoft.Extensions.Configuration;`** to usings if not already present.

6. **Remove `using Amazon;`** only if it's no longer needed (it was needed for `RegionEndpoint` which is now gone from this file). Check if `Amazon` namespace is used elsewhere in the file — if not, remove it.

7. **Fix `ReadStreamAsync`** — replace the current implementation:
```csharp
private static async Task<byte[]> ReadStreamAsync(Stream stream)
{
    using var ms = new MemoryStream();
    await stream.CopyToAsync(ms);
    return ms.ToArray();
}
```
With:
```csharp
private const int MaxImageBytes = 5 * 1024 * 1024;

private static async Task<byte[]> ReadStreamAsync(Stream stream)
{
    using var ms = new MemoryStream();
    await stream.CopyToAsync(ms);
    if (ms.Length > MaxImageBytes)
        throw new InvalidOperationException($"Image exceeds {MaxImageBytes}-byte Rekognition limit.");
    return ms.ToArray();
}
```

---

## File 2: `src/FortressAI.Web/Program.cs`

### Current state around line 144:
```csharp
builder.Services.AddSingleton<ContentModerationService>();
```

### Required change — replace that single line with these 3 registrations:
```csharp
builder.Services.AddSingleton<IAmazonBedrockRuntime>(sp =>
    new AmazonBedrockRuntimeClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));
builder.Services.AddSingleton<IAmazonRekognition>(sp =>
    new AmazonRekognitionClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));
builder.Services.AddSingleton<ContentModerationService>();
```

The `IAmazonBedrockRuntime` interface is from `Amazon.BedrockRuntime` namespace.
The `IAmazonRekognition` interface is from `Amazon.Rekognition` namespace.
Both are already imported in Program.cs (check existing usings — add if missing).

---

## File 3: `src/FortressAI.Web/appsettings.json`

### Current Bedrock section (around line 20):
```json
"Bedrock": {
  "TitleModelId": "us.anthropic.claude-sonnet-4-6",
  "InvokeModelId": "us.anthropic.claude-sonnet-4-5-20250929-v1:0"
},
```

### Required change — add `ModerationModelId` to the existing Bedrock section:
```json
"Bedrock": {
  "TitleModelId": "us.anthropic.claude-sonnet-4-6",
  "InvokeModelId": "us.anthropic.claude-sonnet-4-5-20250929-v1:0",
  "ModerationModelId": "us.anthropic.claude-haiku-4-5-20251001-v1:0"
},
```

Do NOT create a duplicate Bedrock section. Add to the existing one.

---

## After making all changes:

Run `dotnet build src/FortressAI.Web/FortressAI.Web.csproj` and confirm 0 errors.

Report exactly what you changed in each file and confirm the build result.
