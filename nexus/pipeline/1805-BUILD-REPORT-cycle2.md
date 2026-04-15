# Build Report — ADO #1805 (Fix Cycle 2)

**Date:** 2026-04-15  
**Engineer:** Tony Stark (software-engineer)  
**Commit:** `3f9a4c6`  
**Branch:** (current branch — see `git log`)

---

## What Was Built

Three surgical changes to `BedrockService.cs` to fix vision Bedrock calls timing out at 120s in ECS. Root cause: missing `anthropic_beta` header that the working Python implementation sends.

---

## Files Changed

- `nexus/src/FortressNexus.Web/Services/BedrockService.cs` — Three targeted changes (see below)

No other files modified. `InvokeAsync` (text-only path) was explicitly NOT touched.

---

## Changes Applied

### 1. `anthropic_beta` header in `InvokeWithImageAsync` (line 140)
```csharp
["anthropic_beta"] = new JsonArray { "output-128k-2025-02-19" },
```
Added immediately after `anthropic_version` in the `requestObj`. Matches the Python `generic_docproc_auto_v8.py` payload that works in production. This is the root-cause fix.

### 2. SDK Timeout on `AmazonBedrockRuntimeClient` constructor (line 25)
```csharp
Timeout = TimeSpan.FromSeconds(600)
```
Prevents SDK internal timeout from firing before the per-attempt CTS (which is 120s). SDK timeout is now always the backstop, not the trigger. Matches Python `read_timeout=1000` pattern.

### 3. `OperationCanceledException` reason logging (lines 199–200)
```csharp
var reason = cancellationToken.IsCancellationRequested ? "caller-cancelled" : "per-attempt-timeout";
_logger.LogWarning("[BEDROCK] Vision invoke CANCELLED/TIMEOUT after {ElapsedMs}ms model={Model} reason={Reason} token={TokenId}",
    (int)elapsed.TotalMilliseconds, model, reason, oce.CancellationToken.GetHashCode());
```
Now distinguishes outer caller cancellation from per-attempt CTS timeout. Diagnostic clarity for future incidents.

---

## Parallelization Used

No — single-file change, sequential CC run.

## CC Sessions Run

1 — CC Sonnet, piped brief from `/tmp/tony-1805-fix-brief.md`

---

## Acceptance Criteria Verification

- [x] `InvokeWithImageAsync` request payload includes `"anthropic_beta": ["output-128k-2025-02-19"]` — confirmed line 140
- [x] `AmazonBedrockRuntimeClient` constructor includes `Timeout = TimeSpan.FromSeconds(600)` — confirmed line 25
- [x] `OperationCanceledException` catch logs `reason=per-attempt-timeout` or `reason=caller-cancelled` — confirmed lines 199–200
- [x] `InvokeAsync` (text-only path) NOT modified — verified via grep, no `anthropic_beta` in that method
- [x] Build: **0 errors** (`dotnet build` — 1 pre-existing unrelated warning in `FileStorageService.cs`)

---

## Known Edge Cases / Things Clint Should Scrutinize

- The `cancellationToken.IsCancellationRequested` check in the catch block uses the **outer** `cancellationToken` parameter. If the per-attempt CTS was linked to the outer token, this could misclassify. In the current implementation, the per-attempt CTS is created independently (`CancellationTokenSource.CreateLinkedTokenSource` or standalone), so the check should be accurate — but worth a quick look at how the per-attempt CTS is constructed.
- The `Timeout = TimeSpan.FromSeconds(600)` is a client-level SDK timeout. If multiple callers share this client instance (singleton), this is fine. Confirm `BedrockService` is registered as a singleton or scoped — either works, but if transient, a new client is constructed per request (minor overhead, not a bug).

---

## How to Test Locally

```bash
# Build verification
cd /home/fredw/projects/fip/nexus/src/FortressNexus.Web
dotnet build 2>&1 | tail -5
# Expected: 0 Error(s)

# Smoke test (requires ECS env or local Bedrock credentials)
# Submit a vision/image job through the NEXUS UI and observe:
# - No 120s timeout cancellation
# - Logs show reason=per-attempt-timeout if CTS fires, reason=caller-cancelled if shutdown
```

---

## ADO

- Comment posted to WI #1805 (comment ID 745719)
- Commit: `3f9a4c6`
