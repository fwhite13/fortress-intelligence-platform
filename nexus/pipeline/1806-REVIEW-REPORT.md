# Review Report — ADO #1806

**Commit:** `e666175`
**Cycle:** 1
**Risk:** medium — core spec generation path
**Reviewer:** Hawkeye (code-reviewer)
**Date:** 2026-04-13

---

## Verdict: ✅ PASS

---

## Spec Compliance Check

**Files changed:** `BedrockService.cs`, `SpecGenerationService.cs` only.
**Scope:** ✅ Compliant — no out-of-scope files touched.

---

## CC Review Summary

CC was run adversarially against both files with the full checklist. All 5 critical checks and 1 important check returned PASS. Zero false positives dismissed — all checks resolved cleanly. No unexpected findings beyond the pre-acknowledged known issues.

---

## Critical Checks

| # | Check | Result |
|---|-------|--------|
| C1 | Model ID exact: `"us.anthropic.claude-sonnet-4-5-20250929-v1:0"` | ✅ PASS |
| C2 | Retry loop correctness (callTask re-created, visionSucceeded, await gating) | ✅ PASS |
| C3 | Backoff: `3 * attempt` s, `if (attempt < maxAttempts)` guard present | ✅ PASS |
| C4 | `visionResult` default-safe, `.Text` only accessed inside `if (visionSucceeded)` | ✅ PASS |
| C5 | Overall CTS `FromMinutes(10)`, token threaded, log says "10min" | ✅ PASS |

### C1 — Model ID
```csharp
// BedrockService.cs:16
private const string DefaultModelId = "us.anthropic.claude-sonnet-4-5-20250929-v1:0";
```
Region prefix `us.` ✓ · Model name `claude-sonnet-4-5` ✓ · Date `20250929` ✓ · Suffix `-v1:0` ✓

### C2 — Retry loop
- `callTask` declared **inside** the loop body — re-created every iteration. ✓
- `visionSucceeded = true` set only in the non-timeout fall-through, before `break`. ✓
- `await callTask` is unreachable when timeout won (timeout branch exits via `continue`). ✓
- Control flow: timeout → `continue`; success → `visionResult = await callTask; visionSucceeded = true; break`. ✓

### C3 — Backoff
```csharp
if (attempt < maxAttempts)
    await Task.Delay(TimeSpan.FromSeconds(3 * attempt)); // backoff: 3s, 6s
continue;
```
attempt=1 → 3s · attempt=2 → 6s · attempt=3 → no sleep (guard prevents it). ✓

### C4 — visionResult safety
```csharp
(string Text, int PromptTokens, int CompletionTokens) visionResult = default;
...
if (visionSucceeded)
{
    sb.AppendLine($"**Vision Analysis:**");
    sb.AppendLine(visionResult.Text);   // only access point
}
else
{
    sb.AppendLine("*Image vision analysis timed out — skipped.*");  // no visionResult access
}
```
No access to `visionResult` fields when `!visionSucceeded`. ✓

### C5 — Overall CTS
```csharp
using var overallCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
...
var userPrompt = await BuildPromptAsync(submission, systemPrompt, overallCts.Token);
...
_logger.LogError("... Overall generation timeout (10min) ...");
```
10 minutes ✓ · token threaded to BuildPromptAsync ✓ · log updated ✓

---

## Important Checks

| # | Check | Result |
|---|-------|--------|
| I1 | No call site passes hardcoded old model ID | ✅ PASS |
| I2 | Scope: only BedrockService.cs + SpecGenerationService.cs modified | ✅ PASS |

### I1 — Call sites
All `InvokeAsync` / `InvokeWithImageAsync` calls in `SpecGenerationService.cs`:
- Line 79: `_bedrock.InvokeAsync(systemPrompt, userPrompt)` — no explicit `modelId`. Uses default. ✓
- Lines 196–200: `_bedrock.InvokeWithImageAsync(..., imageBytes, file.ContentType)` — no explicit `modelId`. Uses default. ✓

External callers (`DiscoveryService.cs`, `ArtifactGenerationService.cs`) both pass explicit config-driven `modelId` values — unaffected by the `DefaultModelId` change. ✓

---

## Issues Found

**None.** No Critical, Important, or Nitpick issues.

---

## Pre-existing Known Issues (acknowledged, not flagged)

- **Worst-case timeout math:** 5 files × 120s × 3 attempts = 18min > 10min CTS. Intentional by design.
- **Dangling Bedrock call on timeout:** `Task.WhenAny` does not cancel the in-flight SDK call. CancellationToken threading into the vision retry loop is out of scope for this WI.

---

## Positive Observations

- Retry loop structure is clean and correct — no off-by-one, no task reuse bug.
- `visionResult` safety pattern (default + bool guard) is correct and defensive.
- Backoff comment inline (`// backoff: 3s, 6s`) is helpful.
- Both the CTS value and its log message were updated together — no stale "5min" message left in.
- No other files were touched; change is tightly scoped.

---

_Hawkeye — code-reviewer · ADO #1806 · Cycle 1_
