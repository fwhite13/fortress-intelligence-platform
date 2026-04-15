# Review Report — ADO #1805

**NEXUS: Spec Gen Vision — Diagnostic Logging Pass**
**Commit:** `418a71f` — `fix(ADO#1805): add diagnostic logging for vision Bedrock calls`
**Reviewer:** Hawkeye (code-reviewer)
**Date:** 2026-04-15

---

## Verdict: ✅ PASS

---

## CC Review Note

CC Sonnet returned HTTP 500 (internal server error) — same API instability Tony hit during build. Fell back to direct adversarial review per TOOLS.md policy. Full file content was read and all checklist items verified against actual source lines.

---

## Spec Compliance Check

This was a diagnostic-only pass. The acceptance criteria from the task brief were:

- [x] `[BEDROCK] Vision invoke START` with ISO timestamp, model, mimeType, imageBytes — ✅ verified
- [x] `[BEDROCK] Vision invoke COMPLETE` with elapsed ms, promptTokens, completionTokens — ✅ verified
- [x] `[BEDROCK] Vision invoke FAILED` with ErrorCode, StatusCode — ✅ verified
- [x] `[BEDROCK] Vision invoke CANCELLED/TIMEOUT` with elapsed ms — ✅ verified
- [x] `[SPEC_GEN] Vision attempt N/3` with fileId, s3Key, imageBytes, timeoutSeconds — ✅ verified
- [x] `AmazonBedrockRuntimeException` caught before retry — ✅ verified
- [x] Timeout value (`_specGenConfig.TimeoutSeconds`) NOT changed — ✅ confirmed unchanged at default 120s
- [x] `dotnet build` 0 errors — ✅ confirmed (1 pre-existing warning in FileStorageService.cs, unrelated)

**Spec compliance verdict: ✅ COMPLIANT**

---

## Consistency Audit

### Namespace verification
Both files use fully-qualified `Amazon.BedrockRuntime.AmazonBedrockRuntimeException` — **not** the incorrect `Amazon.BedrockRuntime.Model.*` form.

- `BedrockService.cs` has `using Amazon.BedrockRuntime;` at line 5 and uses `Amazon.BedrockRuntime.AmazonBedrockRuntimeException` in catch
- `SpecGenerationService.cs` uses `Amazon.BedrockRuntime.AmazonBedrockRuntimeException` fully-qualified (no using needed) — ✅ correct

### Exception type cross-check
`AmazonBedrockRuntimeException` is in `Amazon.BedrockRuntime` namespace per AWS SDK v3. Both files reference it correctly. Build confirms this (0 errors).

---

## Check Results

### ✅ Check 1: `invokeStart` declared before try block
**Line 160, BedrockService.cs:**
```csharp
var invokeStart = DateTimeOffset.UtcNow;
_logger.LogInformation("[BEDROCK] Vision invoke START ...", invokeStart, ...);

try
{
    ...
}
catch (Amazon.BedrockRuntime.AmazonBedrockRuntimeException bedrockEx)
{
    ...  // invokeStart in scope ✅
}
catch (OperationCanceledException)
{
    var elapsed = DateTimeOffset.UtcNow - invokeStart;  // in scope ✅
```
`invokeStart` is declared before the `try` at line 160, `try` opens at line 164. All catch blocks can compute elapsed. ✅

---

### ✅ Check 2: Exception catch order
**BedrockService.cs** — order is:
1. `catch (Amazon.BedrockRuntime.AmazonBedrockRuntimeException bedrockEx)` — line 188
2. `catch (OperationCanceledException)` — line 194
3. `catch (Exception ex)` — line 201

**SpecGenerationService.cs** — order inside vision retry loop is:
1. `catch (Amazon.BedrockRuntime.AmazonBedrockRuntimeException bedrockEx)` — line 257
2. `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` — line 264

Both files: `AmazonBedrockRuntimeException` appears before `OperationCanceledException`. ✅

---

### ✅ Check 3: AmazonBedrockRuntimeException behavior
**BedrockService.cs** — catches, logs, then `throw;` (re-throws):
```csharp
catch (Amazon.BedrockRuntime.AmazonBedrockRuntimeException bedrockEx)
{
    _logger.LogError("[BEDROCK] Vision invoke FAILED ...", bedrockEx.ErrorCode, (int)bedrockEx.StatusCode, bedrockEx.Message, model);
    throw;
}
```
✅ Re-throws — propagates up to SpecGenerationService.

**SpecGenerationService.cs** — catches, logs, then `break;` (exits retry loop, no re-throw):
```csharp
catch (Amazon.BedrockRuntime.AmazonBedrockRuntimeException bedrockEx)
{
    _logger.LogError("[SPEC_GEN] Vision Bedrock error (attempt {Attempt}/{Max}) ...", ...);
    // Bedrock errors (throttling, auth, model access) — don't retry, break out
    break;
}
```
✅ Breaks — falls through to `visionSucceeded = false` path. Non-retryable errors don't blow up entire spec gen.

---

### ✅ Check 4: Timeout value unchanged
`SpecGenerationService.cs` line 238:
```csharp
attemptCts.CancelAfter(TimeSpan.FromSeconds(_specGenConfig.TimeoutSeconds));
```
`_specGenConfig.TimeoutSeconds` verified — `SpecGenInferenceConfig.TimeoutSeconds` property exists with default value 120, is populated from IOptions injection. No hardcoded value, no change. ✅

The log line at 243 also reads `_specGenConfig.TimeoutSeconds` (diagnostic output), not a hardcoded literal. ✅

---

### ✅ Check 5: Namespace correctness
- `using Amazon.BedrockRuntime;` present in BedrockService.cs (line 5)
- Both catch sites use `Amazon.BedrockRuntime.AmazonBedrockRuntimeException` (NOT `.Model.`)
- Build succeeded — compiler confirms namespace is correct ✅

---

### ✅ Check 6: Structured log placeholder counts

| Call | Format string placeholders | Arguments | Match |
|------|---------------------------|-----------|-------|
| START | `{Timestamp:O}`, `{Model}`, `{MimeType}`, `{Bytes}`, `{MaxTokens}` = 5 | `invokeStart`, `model`, `mimeType`, `imageBytes.Length`, `maxTokens` = 5 | ✅ |
| COMPLETE | `{ElapsedMs}ms`, `{Model}`, `{Pt}`, `{Ct}` = 4 | `(int)elapsed.TotalMilliseconds`, `model`, `promptTokens`, `completionTokens` = 4 | ✅ |
| FAILED | `{ErrorCode}`, `{StatusCode}`, `{Message}`, `{Model}` = 4 | `bedrockEx.ErrorCode`, `(int)bedrockEx.StatusCode`, `bedrockEx.Message`, `model` = 4 | ✅ |
| CANCELLED/TIMEOUT | `{ElapsedMs}ms`, `{Model}` = 2 | `(int)elapsed.TotalMilliseconds`, `model` = 2 | ✅ |
| UNEXPECTED EXCEPTION | `{ElapsedMs}ms`, `{ExType}`, `{Model}` = 3 | `(int)elapsed.TotalMilliseconds`, `ex.GetType().FullName`, `model` = 3 | ✅ |
| SPEC_GEN Vision attempt | `{Attempt}`, `{Max}`, `{FileId}`, `{S3Key}`, `{Bytes}`, `{TimeoutS}s` = 6 | `attempt`, `maxAttempts`, `file.Id`, `file.S3Key`, `imageBytes.Length`, `_specGenConfig.TimeoutSeconds` = 6 | ✅ |
| SPEC_GEN Bedrock error | `{Attempt}`, `{Max}`, `{FileId}`, `{ErrorCode}`, `{StatusCode}` = 5 | `attempt`, `maxAttempts`, `file.Id`, `bedrockEx.ErrorCode`, `(int)bedrockEx.StatusCode` = 5 | ✅ |

No structured logging holes. ✅

---

### ✅ Check 7: Build result
```
Build succeeded.
/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/FileStorageService.cs(148,27): warning CS8601
    1 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.57
```
Pre-existing warning in FileStorageService.cs — unrelated to this change. 0 errors. ✅

---

### ✅ Check 8: InvokeAsync untouched
`BedrockService.cs` `InvokeAsync` method is unchanged. No timing code, no try/catch wrapper, same log calls as before. The diff only touches `InvokeWithImageAsync`. ✅

---

### ✅ Check 9: No unintended changes
Commit stat: 2 files changed, 52 insertions(+), 14 deletions(-). The 14 deletions are the old single-logger + bare `InvokeModelAsync` call that got replaced by the instrumented try/catch block. No constructor changes, no unrelated method touches, no using statement drift. ✅

---

## Issues Found

**Critical:** 0
**Important:** 0
**Nitpick:** 1

### N1: BedrockService.cs FAILED log doesn't include elapsed (minor)
- **File:** `BedrockService.cs` (line 189–192)
- **Issue:** The `[BEDROCK] Vision invoke FAILED` log for `AmazonBedrockRuntimeException` doesn't include elapsed time. The CANCELLED/TIMEOUT and UNEXPECTED EXCEPTION paths both log elapsed, but the AWS error path does not. Since AWS errors (AccessDeniedException etc.) may fail instantly while timeouts take 60s+, elapsed would confirm whether the 60s+ latency is a Bedrock API error vs. a timeout.
- **Impact:** Not a correctness issue — the START log exists so CloudWatch can correlate timestamps. The data is derivable. Not blocking.
- **Suggested fix (optional):**
  ```diff
  - _logger.LogError("[BEDROCK] Vision invoke FAILED — AmazonBedrockRuntimeException: ErrorCode={ErrorCode} StatusCode={StatusCode} Message={Message} model={Model}",
  -     bedrockEx.ErrorCode, (int)bedrockEx.StatusCode, bedrockEx.Message, model);
  + _logger.LogError("[BEDROCK] Vision invoke FAILED — AmazonBedrockRuntimeException: elapsed={ElapsedMs}ms ErrorCode={ErrorCode} StatusCode={StatusCode} Message={Message} model={Model}",
  +     (int)(DateTimeOffset.UtcNow - invokeStart).TotalMilliseconds, bedrockEx.ErrorCode, (int)bedrockEx.StatusCode, bedrockEx.Message, model);
  ```
  Nice-to-have for cycle 2 if we need it, but CloudWatch START timestamp correlation makes this derivable. **Not blocking.**

---

## Positive Observations

- `invokeStart` placement is textbook correct — declared outside try, available in all catch blocks.
- Exception catch ordering is correct in both files.
- The asymmetric behavior (re-throw in BedrockService, break in SpecGenService) is the right design — non-retryable errors don't cascade to a spec-gen failure when vision analysis is optional.
- Zero log placeholder mismatches across 7 new log calls.
- The double-log pattern (both layers log AWS errors) is deliberate and useful for CloudWatch correlation. The `[BEDROCK]` / `[SPEC_GEN]` prefix distinction ensures they're distinguishable.
- Timeout value correctly reads from config, not hardcoded. No regression risk.

---

## Summary

Clean diagnostic-only pass. All structural requirements verified: invokeStart placement, exception ordering, correct throw vs. break behavior, namespace correctness, structured log integrity, build clean. One nitpick (no elapsed in the AWS FAILED log) is not blocking — the diagnostic data will be sufficient from CloudWatch START/COMPLETE/FAILED timestamps.

**Ships as-is. Confirmed ready for ECS deploy.**
