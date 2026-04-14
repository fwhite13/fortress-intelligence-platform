# Build Report — ADO #1806 — Vision calls timing out (model ID + timeout + retry)

**Commit:** `e666175`
**Date:** 2026-04-13
**Engineer:** Tony Stark (software-engineer)
**Risk:** Medium — core spec generation path

---

## What Was Built

Three targeted fixes to the NEXUS vision pipeline:
1. Updated `BedrockService.DefaultModelId` to the confirmed-working `sonnet-4-5-20250929-v1:0`
2. Bumped per-call vision timeout from 60s → 120s
3. Added retry loop (3 attempts, 3s/6s exponential backoff) around vision calls
4. Bumped overall `SpecGenerationService` timeout from 5min → 10min

---

## Files Changed

| File | Change |
|------|--------|
| `Services/BedrockService.cs` | `DefaultModelId` → `us.anthropic.claude-sonnet-4-5-20250929-v1:0` (fixes all call sites — both `InvokeAsync` and `InvokeWithImageAsync`) |
| `Services/SpecGenerationService.cs` | Overall CTS: 5min → 10min; vision block: 60s→120s + single attempt→3 attempts with 3s/6s backoff |

---

## Parallelization

Not applicable — serial single-file edits with no shared state.

---

## CC Sessions

1 CC session (sonnet). Produced clean output on first pass.

---

## Acceptance Criteria

- [x] `BedrockService.DefaultModelId` = `us.anthropic.claude-sonnet-4-5-20250929-v1:0` — verified via grep
- [x] Vision timeout = 120s — verified at line 201
- [x] Retry loop: 3 attempts, 3s/6s backoff — verified lines 192–213
- [x] Overall timeout = 10min — verified at line 52
- [x] `dotnet build` — 0 errors, 0 warnings

---

## Known Edge Cases / Things Clint Should Scrutinize

- **Overall 10min timeout vs. worst-case math:** Worst case is 5 files × 120s × 3 attempts = 18min. The 10min overall CTS will still cancel at 10min. This is intentional — we prefer cancelling a runaway generation over hanging indefinitely. Real-world vision calls that succeed will be much faster.
- **`Task.WhenAny` fire-and-forget risk:** When `callTask` loses the race to `timeoutTask`, the Bedrock SDK call continues running in the background. This is pre-existing behavior (not introduced here) — the SDK call is unobservable and will eventually complete or timeout at the AWS SDK level. No action needed unless we want to cancel the SDK calls, which would require adding `CancellationToken` plumbing to `BedrockService.InvokeWithImageAsync`.
- **`visionResult = default` on struct:** `(string Text, int PromptTokens, int CompletionTokens)` is a value tuple. Default gives `null`/`0`/`0` — only accessed behind `if (visionSucceeded)`, so this is safe.

---

## How to Test Locally

1. Submit a feature request with one or more image attachments in NEXUS
2. Trigger spec generation
3. Watch CloudWatch/console logs for `[SPEC_GEN] Vision call timed out (attempt X/3)` if model is slow
4. Verify retry fires and either succeeds or gracefully skips with `*Image vision analysis timed out — skipped.*`

---

## Build Output

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:04.21
```
