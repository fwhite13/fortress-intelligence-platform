# Build Report — ADO #1805

**NEXUS: Spec Gen Vision — Diagnostic Logging Pass**

---

## What was built

Added detailed pre/post diagnostic logging around all Bedrock vision (`InvokeWithImageAsync`) calls in `BedrockService` and `SpecGenerationService`. This is a diagnostic-only pass — no timeout values were changed. Goal: expose exact elapsed time, model, image size, and AWS error codes in CloudWatch so we can confirm whether the 10x latency discrepancy (6s external vs 60s+ ECS) is caused by NAT routing through `vpc-0783a9844741980ff` (no Bedrock VPC endpoint).

---

## Files changed

- `nexus/src/FortressNexus.Web/Services/BedrockService.cs`
  - Replaced the single existing `LogInformation` + bare `InvokeModelAsync` call with a fully-instrumented block:
    - `invokeStart` declared before try block (catch blocks can compute elapsed)
    - `[BEDROCK] Vision invoke START` log with ISO 8601 timestamp, model ID, mimeType, imageBytes, maxTokens
    - `[BEDROCK] Vision invoke COMPLETE` log with elapsed ms, promptTokens, completionTokens (on success)
    - `catch (AmazonBedrockRuntimeException)` — logs ErrorCode + StatusCode + Message, re-throws
    - `catch (OperationCanceledException)` — logs elapsed ms, re-throws
    - `catch (Exception)` — logs elapsed ms + ExceptionType.FullName, re-throws

- `nexus/src/FortressNexus.Web/Services/SpecGenerationService.cs`
  - Added `[SPEC_GEN] Vision attempt N/3` log before `InvokeWithImageAsync` call — includes fileId, s3Key, imageBytes, timeoutSeconds
  - Added `catch (AmazonBedrockRuntimeException)` before the existing `catch (OperationCanceledException)` — logs ErrorCode + StatusCode, then **breaks** (no retry on hard AWS errors like ThrottlingException, AccessDeniedException, model not enabled)

---

## Parallelization used

No — single sequential CC pass over two files.

---

## CC sessions

CC Sonnet returned HTTP 500 (internal server error) on two consecutive attempts (API instability, not rate limit). Fell back to direct file edits per TOOLS.md policy. Changes are precise surgical edits matching the brief spec exactly.

---

## Acceptance criteria verification

- [x] `[BEDROCK] Vision invoke START` with ISO timestamp, model ID, image byte count — ✅ implemented
- [x] `[BEDROCK] Vision invoke COMPLETE` with elapsed ms — ✅ implemented (success path)
- [x] `[BEDROCK] Vision invoke FAILED` with ErrorCode, StatusCode — ✅ implemented (AmazonBedrockRuntimeException catch)
- [x] `[BEDROCK] Vision invoke CANCELLED/TIMEOUT` with elapsed ms — ✅ implemented (OperationCanceledException catch)
- [x] `[SPEC_GEN] Vision attempt N/3` with fileId, s3Key, imageBytes, timeout seconds — ✅ implemented
- [x] `AmazonBedrockRuntimeException` caught with ErrorCode + StatusCode before re-throwing — ✅ (BedrockService re-throws; SpecGenerationService breaks without retry)
- [x] `invokeStart` declared before try block — ✅ confirmed
- [x] Timeout value (`_specGenConfig.TimeoutSeconds`) NOT changed — ✅ untouched
- [x] `dotnet build` with 0 errors — ✅ confirmed (1 pre-existing warning in FileStorageService.cs, unrelated)

---

## Known edge cases / things Clint should scrutinize

1. **Namespace fix required:** The brief specified `Amazon.BedrockRuntime.Model.AmazonBedrockRuntimeException` but the AWSSDK v3 package places this in `Amazon.BedrockRuntime` (not `.Model`). First build surfaced the error; corrected to `Amazon.BedrockRuntime.AmazonBedrockRuntimeException`. Both files use the same corrected namespace.

2. **SpecGenerationService `AmazonBedrockRuntimeException` break vs re-throw:** Per brief spec — hard AWS errors break the retry loop and fall through to the `visionSucceeded = false` path (logs "skipped"), they do NOT propagate to the outer `catch (Exception)`. This is intentional — a ThrottlingException or AccessDeniedException should not blow up spec gen entirely when the vision analysis is optional.

3. **BedrockService re-throw behaviour:** In `BedrockService`, `AmazonBedrockRuntimeException` is caught, logged, and re-thrown — so it will still propagate up to `SpecGenerationService`'s `AmazonBedrockRuntimeException` catch there. Both log entries will appear in CloudWatch for AWS errors — this is intentional (two layers = more data).

---

## How to test locally

CloudWatch logs won't be available locally, but build + smoke test:

```bash
cd /home/fredw/projects/fip/nexus/src/FortressNexus.Web
dotnet build  # should show 0 errors, 1 pre-existing warning
```

For CloudWatch validation — deploy to ECS and trigger a spec gen with an image attachment. Search logs for `[BEDROCK] Vision invoke START` and `[SPEC_GEN] Vision attempt` to confirm instrumentation is live.

---

## Commit

```
418a71f  fix(ADO#1805): add diagnostic logging for vision Bedrock calls
```

**2 files changed, 52 insertions(+), 14 deletions(-)**

---

## Diff

```diff
diff --git a/nexus/src/FortressNexus.Web/Services/BedrockService.cs b/nexus/src/FortressNexus.Web/Services/BedrockService.cs
index a1c286f..2795c91 100644
--- a/nexus/src/FortressNexus.Web/Services/BedrockService.cs
+++ b/nexus/src/FortressNexus.Web/Services/BedrockService.cs
@@ -157,26 +157,54 @@ public class BedrockService : IDisposable
             Body = new MemoryStream(Encoding.UTF8.GetBytes(json))
         };
 
-        _logger.LogInformation("[BEDROCK] Invoking model {Model} with image ({MimeType}, {Bytes} bytes), maxTokens={MaxTokens}",
-            model, mimeType, imageBytes.Length, maxTokens);
+        var invokeStart = DateTimeOffset.UtcNow;
+        _logger.LogInformation("[BEDROCK] Vision invoke START {Timestamp:O} model={Model} mimeType={MimeType} imageBytes={Bytes} maxTokens={MaxTokens}",
+            invokeStart, model, mimeType, imageBytes.Length, maxTokens);
 
-        var response = await _client.InvokeModelAsync(request, cancellationToken);
-        var responseJson = await new StreamReader(response.Body).ReadToEndAsync();
+        try
+        {
+            var response = await _client.InvokeModelAsync(request, cancellationToken);
+            var responseJson = await new StreamReader(response.Body).ReadToEndAsync();
 
-        using var doc = JsonDocument.Parse(responseJson);
-        var root = doc.RootElement;
+            using var doc = JsonDocument.Parse(responseJson);
+            var root = doc.RootElement;
 
-        var text = root.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
+            var text = root.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
 
-        int promptTokens = 0;
-        int completionTokens = 0;
-        if (root.TryGetProperty("usage", out var usage))
+            int promptTokens = 0;
+            int completionTokens = 0;
+            if (root.TryGetProperty("usage", out var usage))
+            {
+                if (usage.TryGetProperty("input_tokens", out var it)) promptTokens = it.GetInt32();
+                if (usage.TryGetProperty("output_tokens", out var ot)) completionTokens = ot.GetInt32();
+            }
+
+            var elapsed = DateTimeOffset.UtcNow - invokeStart;
+            _logger.LogInformation("[BEDROCK] Vision invoke COMPLETE elapsed={ElapsedMs}ms model={Model} promptTokens={Pt} completionTokens={Ct}",
+                (int)elapsed.TotalMilliseconds, model, promptTokens, completionTokens);
+
+            return (text, promptTokens, completionTokens);
+        }
+        catch (Amazon.BedrockRuntime.AmazonBedrockRuntimeException bedrockEx)
         {
-            if (usage.TryGetProperty("input_tokens", out var it)) promptTokens = it.GetInt32();
-            if (usage.TryGetProperty("output_tokens", out var ot)) completionTokens = ot.GetInt32();
+            _logger.LogError("[BEDROCK] Vision invoke FAILED — AmazonBedrockRuntimeException: ErrorCode={ErrorCode} StatusCode={StatusCode} Message={Message} model={Model}",
+                bedrockEx.ErrorCode, (int)bedrockEx.StatusCode, bedrockEx.Message, model);
+            throw;
+        }
+        catch (OperationCanceledException)
+        {
+            var elapsed = DateTimeOffset.UtcNow - invokeStart;
+            _logger.LogWarning("[BEDROCK] Vision invoke CANCELLED/TIMEOUT after {ElapsedMs}ms model={Model}",
+                (int)elapsed.TotalMilliseconds, model);
+            throw;
+        }
+        catch (Exception ex)
+        {
+            var elapsed = DateTimeOffset.UtcNow - invokeStart;
+            _logger.LogError(ex, "[BEDROCK] Vision invoke UNEXPECTED EXCEPTION after {ElapsedMs}ms ExceptionType={ExType} model={Model}",
+                (int)elapsed.TotalMilliseconds, ex.GetType().FullName, model);
+            throw;
         }
-
-        return (text, promptTokens, completionTokens);
     }
 
     public void Dispose() => _client.Dispose();
diff --git a/nexus/src/FortressNexus.Web/Services/SpecGenerationService.cs b/nexus/src/FortressNexus.Web/Services/SpecGenerationService.cs
index 9d7178e..ff54474 100644
--- a/nexus/src/FortressNexus.Web/Services/SpecGenerationService.cs
+++ b/nexus/src/FortressNexus.Web/Services/SpecGenerationService.cs
@@ -239,6 +239,9 @@ public class SpecGenerationService : ISpecGenerationService
 
                                 try
                                 {
+                                    _logger.LogInformation("[SPEC_GEN] Vision attempt {Attempt}/{Max} fileId={FileId} s3Key={S3Key} imageBytes={Bytes} timeout={TimeoutS}s",
+                                        attempt, maxAttempts, file.Id, file.S3Key, imageBytes.Length, _specGenConfig.TimeoutSeconds);
+
                                     visionResult = await _bedrock.InvokeWithImageAsync(
                                         systemPrompt,
                                         $"Describe what you see in this UI mockup image for the feature: {submission.Title}",
@@ -251,6 +254,13 @@ public class SpecGenerationService : ISpecGenerationService
                                     visionSucceeded = true;
                                     break;
                                 }
+                                catch (Amazon.BedrockRuntime.AmazonBedrockRuntimeException bedrockEx)
+                                {
+                                    _logger.LogError("[SPEC_GEN] Vision Bedrock error (attempt {Attempt}/{Max}) fileId={FileId}: ErrorCode={ErrorCode} StatusCode={StatusCode}",
+                                        attempt, maxAttempts, file.Id, bedrockEx.ErrorCode, (int)bedrockEx.StatusCode);
+                                    // Bedrock errors (throttling, auth, model access) — don't retry, break out
+                                    break;
+                                }
                                 catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                                 {
                                     // Per-attempt timeout (not overall CTS cancel)
```
