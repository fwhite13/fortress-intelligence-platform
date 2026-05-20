# Adversarial Review Brief: ADO#3824 Cycle 2 — ContentModerationService

## Task
Verify Tony's fixes for C1 issues I1–I4, then re-check the full service.

## Files to Read
1. `src/FortressAI.Web/Services/ContentModerationService.cs` — Read the ENTIRE file
2. `src/FortressAI.Web/Program.cs` — Search for IAmazonBedrockRuntime, IAmazonRekognition, AddSingleton, AWS:Region registrations
3. `src/FortressAI.Web/appsettings.json` — Look for Bedrock section and ModerationModelId

## C1 Fix Verification (verify each one explicitly)

### I3 — DI Pattern (root fix)
VERIFY:
- `Program.cs` registers `IAmazonBedrockRuntime` via `builder.Services.AddSingleton<IAmazonBedrockRuntime>(...)` 
- `Program.cs` registers `IAmazonRekognition` via `builder.Services.AddSingleton<IAmazonRekognition>(...)`
- Region sourced from `builder.Configuration["AWS:Region"] ?? "us-east-1"` (NOT hardcoded)
- `ContentModerationService` constructor takes `IAmazonBedrockRuntime bedrockClient, IAmazonRekognition rekognitionClient, IConfiguration configuration, ILogger<ContentModerationService> logger`
- NO `new AmazonBedrockRuntimeClient(...)` anywhere in the service constructor
- NO `new AmazonRekognitionClient(...)` anywhere in the service constructor

### I1 — Model ID from config
VERIFY:
- `appsettings.json` has `"ModerationModelId"` key under `"Bedrock"` section
- The value is `"us.anthropic.claude-haiku-4-5-20251001-v1:0"` or similar (not hardcoded in service)
- `ContentModerationService` reads model ID via `_configuration["Bedrock:ModerationModelId"]` with a fallback
- NO hardcoded model ID string literal (like `"us.anthropic.claude-haiku..."`) in the service class itself

### I2 — No hardcoded region
VERIFY:
- NO `RegionEndpoint.USEast1` anywhere in ContentModerationService.cs
- NO `RegionEndpoint` usage at all in ContentModerationService.cs
- Region resolved through DI (covered by I3 fix)

### I4 — 5MB image size guard
VERIFY:
- `private const int MaxImageBytes = 5 * 1024 * 1024;` constant exists in the service
- `ReadStreamAsync` method checks `ms.Length > MaxImageBytes`
- Throws `InvalidOperationException` when exceeded (exact exception type matters)
- That exception is caught by `catch (Exception ex)` in `CheckImageAsync` → fail-open (returns false, not rethrow)

## Re-check Areas (verify all)

### Fail-open paths
- `CheckImageAsync`: both Rekognition path AND Bedrock path must catch ALL exceptions and return false, never throw
- `CheckNameAsync`: must catch ALL exceptions and return false/true (fail-open), never throw

### Haiku prompt text
- Read the exact prompt string passed to Bedrock in CheckImageAsync
- It should instruct Claude to respond with ONLY "PASS" or "FAIL"

### PASS/FAIL parsing
- After calling Bedrock, how is the response parsed?
- It should check for "FAIL" in the response text
- Logic: if response contains "FAIL" → moderation triggered (return false meaning flagged)
- OR: if response contains "PASS" → safe (return true meaning allowed)
- Verify the boolean polarity is correct

### MinConfidence=70f
- Rekognition call should use `MinConfidence = 70f` in the `DetectModerationLabelsRequest`
- NOT 0.7f or 70.0 or any other value

### 100-char truncation in CheckNameAsync
- The name input to CheckNameAsync should be truncated to 100 characters before use
- Verify: `name.Length > 100 ? name.Substring(0, 100) : name` or similar

### Singleton registration still in Program.cs
- Both `IAmazonBedrockRuntime` and `IAmazonRekognition` registered as Singletons (not Scoped/Transient)
- `ContentModerationService` itself registered (what lifetime? should be consistent)

## Adversarial Questions
1. Is the PASS/FAIL boolean logic inverted? (a common bug — returning "true" when content is flagged instead of false)
2. Does `ReadStreamAsync` properly close/dispose the MemoryStream after checking size?
3. Is there any code path in `CheckImageAsync` that could throw an unhandled exception past the catch block?
4. Does the config fallback for model ID use the right config key format (`Bedrock:ModerationModelId`)?
5. Are there any `async void` methods that would swallow exceptions?

## Pass Criteria
- ALL of I1, I2, I3, I4 fixes confirmed present and correct
- Zero new Critical issues
- Zero new Important issues (or note any found)
- dotnet build passes (run: `dotnet build src/FortressAI.Web/FortressAI.Web.csproj --no-restore 2>&1 | tail -5`)

## Output Format
For each check: state PASS or FAIL with evidence (file, line number, exact code snippet).
Be adversarial — look for partial fixes, wrong types, wrong config keys, inverted logic.
End with a summary verdict: PASS / NEEDS-CHANGES / FAIL
