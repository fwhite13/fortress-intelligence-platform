# Review Report — ADO #1812
## CancellationToken fix for Bedrock vision calls (NEXUS)

**Verdict: NEEDS-CHANGES**
**Cycle:** 1
**Reviewer:** Hawkeye
**Commit:** `210da62`

---

## Spec Compliance Check

**§2 Codebase Map:** `BedrockService.cs`, `SpecGenerationService.cs` — both modified as described.

**§6 Out of Scope:** No out-of-scope files touched.

**§7 Acceptance Criteria:**
- ✅ `CancellationToken` parameter added to both `InvokeAsync` and `InvokeWithImageAsync`
- ✅ `Task.WhenAny` / `timeoutTask` fully removed from vision block
- ✅ `CreateLinkedTokenSource` per-attempt inside loop, disposed with `using`
- ✅ `CancelAfter` uses `_specGenConfig.TimeoutSeconds` (not hardcoded)
- ✅ SDK call receives `attemptCts.Token`
- ❌ Outer `catch (Exception ex)` swallows `OperationCanceledException` from overall CTS — overall timeout silently bypassed for image submissions
- ❌ Primary `_bedrock.InvokeAsync(...)` call (line 83) omits `overallCts.Token` — main spec-gen call is outside the overall timeout

**Spec compliance verdict:** ❌ NON-COMPLIANT — 2 defects block PASS

---

## CC Review Summary

CC reviewed both files in full against the 9-point checklist. 7 of 9 checks passed cleanly. CC identified 2 real defects:

1. **C3 — catch swallows overall cancellation (Critical):** The vision block's outer `catch (Exception ex)` at `SpecGenerationService.cs` line ~238 has no guard against `OperationCanceledException`. When the outer `overallCts` fires (10-minute overall timeout), the `OperationCanceledException` propagates out of the retry loop (correct — inner catch guard rejects it), but is then swallowed by the outer `catch (Exception ex)` that wraps the entire vision processing block. Processing continues on subsequent files. The `catch (OperationCanceledException) when (overallCts.IsCancellationRequested)` at the top of `GenerateAsync` **never fires** for submissions containing image files. The overall timeout is silently defeated.

2. **I9 — Main InvokeAsync call missing token (Important):** Line 83 of `SpecGenerationService.cs` calls `_bedrock.InvokeAsync(systemPrompt, userPrompt, _specGenConfig.MaxTokens, _specGenConfig.ModelId)` — no `cancellationToken` argument. Defaults to `CancellationToken.None`. The primary spec-generation Bedrock call (the one that processes the full assembled prompt) is completely outside the overall timeout. A hung Bedrock call here will never be cancelled.

No false positives. All 7 passing checks are genuinely clean.

---

## Consistency Audit

| Check | Result |
|-------|--------|
| `BedrockService.InvokeAsync` → `_client.InvokeModelAsync(request, cancellationToken)` | ✅ Pass |
| `BedrockService.InvokeWithImageAsync` → `_client.InvokeModelAsync(request, cancellationToken)` | ✅ Pass |
| `InvokeWithImageAsync` fallback → `InvokeAsync(..., cancellationToken)` | ✅ Pass |
| `attemptCts.Token` flows to `InvokeWithImageAsync` → SDK | ✅ Pass |
| `SpecGenerationService` line 83 `InvokeAsync` caller | ❌ Missing `overallCts.Token` |
| Overall `catch (Exception ex)` vision wrapper | ❌ Swallows `OperationCanceledException` |

---

## Critical Issues [1]

### C1: `catch (Exception ex)` swallows overall-CTS cancellation in vision block

- **File:** `SpecGenerationService.cs`
- **Location:** Outer `catch (Exception ex)` wrapping the entire `FileType.Image` case
- **Category:** correctness / async cancellation
- **Issue:** The outer catch has no `when` guard. When the overall 10-minute `overallCts` fires while a vision attempt is in progress, the `OperationCanceledException` correctly bypasses the inner per-attempt catch (its `when (!cancellationToken.IsCancellationRequested)` guard returns false). But that exception then propagates out of the for loop and is caught by the outer `catch (Exception ex)`, which logs a warning and appends a "skipped" note. Execution continues to the next file. The `catch (OperationCanceledException) when (overallCts.IsCancellationRequested)` in `GenerateAsync` is never reached. The 10-minute budget is now a suggestion, not an enforced limit.
- **Evidence:**
  ```csharp
  // Outer vision catch — no guard:
  catch (Exception ex)
  {
      _logger.LogWarning(ex, "[SPEC_GEN] Vision call failed for file {S3Key}", file.S3Key);
      sb.AppendLine("*Image vision analysis failed — skipped.*");
  }
  ```
- **Impact:** Submissions with image files can hang indefinitely past the 10-minute overall timeout. Zombie Bedrock calls, wasted resources, and stuck submissions.
- **Fix:**
  ```diff
  - catch (Exception ex)
  + catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
  ```
  This allows the outer `catch (OperationCanceledException) when (overallCts.IsCancellationRequested)` up in `GenerateAsync` to fire correctly when the overall timeout trips.

---

## Important Issues [1]

### I1: Primary `InvokeAsync` call missing `overallCts.Token`

- **File:** `SpecGenerationService.cs`
- **Location:** Line ~83, inside `GenerateAsync`, after the `BuildPromptAsync` call
- **Category:** correctness / async cancellation
- **Issue:** The primary Bedrock call that generates the spec receives no cancellation token (defaults to `CancellationToken.None`). The overall 10-minute budget does not apply to this call. A hung or slow Bedrock response here will block indefinitely.
- **Evidence:**
  ```csharp
  // Missing cancellationToken:
  var result = await _bedrock.InvokeAsync(systemPrompt, userPrompt, _specGenConfig.MaxTokens, _specGenConfig.ModelId);
  ```
- **Impact:** If the main spec-gen Bedrock call hangs (network blip, service degradation), no timeout fires. Submission stays stuck in `Generating` forever.
- **Fix:**
  ```diff
  - var result = await _bedrock.InvokeAsync(systemPrompt, userPrompt, _specGenConfig.MaxTokens, _specGenConfig.ModelId);
  + var result = await _bedrock.InvokeAsync(systemPrompt, userPrompt, _specGenConfig.MaxTokens, _specGenConfig.ModelId, overallCts.Token);
  ```

---

## Nitpicks [0]

None.

---

## Positive Observations

- The per-attempt `CreateLinkedTokenSource` inside the loop with `using var` is the correct pattern — no CTS state bleeds between retries.
- The `when (!cancellationToken.IsCancellationRequested)` guard on the inner catch is precisely right.
- `CancelAfter` using `_specGenConfig.TimeoutSeconds` (not hardcoded 120) is correct.
- Backoff `Task.Delay` correctly passes `cancellationToken` (outer), not `attemptCts.Token`.
- `Task.WhenAny` pattern fully excised — zero traces.
- Token threading through `InvokeWithImageAsync` fallback path is clean.

---

## What to Fix

Tony — two changes needed, both in `SpecGenerationService.cs`:

**Fix 1** — Add a `when` guard to the outer vision exception catch to let overall-CTS cancellation propagate:
```diff
- catch (Exception ex)
+ catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
  {
      _logger.LogWarning(ex, "[SPEC_GEN] Vision call failed for file {S3Key}", file.S3Key);
      sb.AppendLine("*Image vision analysis failed — skipped.*");
  }
```
Note: `cancellationToken` here is the parameter passed to `BuildPromptAsync`, which is `overallCts.Token` from the caller.

**Fix 2** — Pass `overallCts.Token` to the primary `InvokeAsync` call:
```diff
- var result = await _bedrock.InvokeAsync(systemPrompt, userPrompt, _specGenConfig.MaxTokens, _specGenConfig.ModelId);
+ var result = await _bedrock.InvokeAsync(systemPrompt, userPrompt, _specGenConfig.MaxTokens, _specGenConfig.ModelId, overallCts.Token);
```

Both fixes are one-liners. No architectural changes needed.

---

## Cycle 2

**Verdict: ✅ PASS**
**Reviewer:** Hawkeye
**Commit:** `210a529`
**Date:** 2026-04-13

---

### Spec Compliance Check

Two targeted fixes from Cycle 1 NEEDS-CHANGES verified:

**C1 — Outer vision `catch` `when` guard:**
- `catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)` — present verbatim at line 238 ✅
- Operator logic: catches `(not OCE) || (OCE but overall token NOT cancelled)`. Propagates only: OCE + overall token IS cancelled. Correct. ✅
- `cancellationToken` in the `when` guard resolves to `overallCts.Token` — confirmed via `BuildPromptAsync` call site at line 65. ✅
- No bare `catch (Exception ex)` remaining in the vision block. ✅

**I1 — Primary `InvokeAsync` 5th argument:**
- `_bedrock.InvokeAsync(systemPrompt, userPrompt, _specGenConfig.MaxTokens, _specGenConfig.ModelId, overallCts.Token)` at line 83 ✅
- 5th argument is `overallCts.Token` — not `CancellationToken.None`, not `attemptCts.Token`. ✅
- `overallCts` confirmed in scope (created line 56, same method). ✅

### Regression Check

| Catch | Status |
|-------|--------|
| Inner `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` (line 217) | ✅ Intact |
| Top-level `catch (OperationCanceledException) when (overallCts.IsCancellationRequested)` (line 116) | ✅ Intact |
| Discovery context `catch (Exception ex)` (line 76) | ✅ Unchanged |
| Outer `GenerateAsync` `catch (Exception ex)` (line 123) | ✅ Unchanged |

### CC Review Summary

CC read `SpecGenerationService.cs` in full. All 4 checks passed. No false positives. Both one-liner fixes are precisely correct. No regressions. PASS is clean.
