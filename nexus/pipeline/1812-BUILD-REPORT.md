# Build Report — ADO #1812 — CancellationToken Fix for Vision Calls

**Date:** 2026-04-13  
**Engineer:** Tony Stark (software-engineer)  
**Commit:** `210da62`  
**Branch:** main  
**Build:** ✅ 0 errors, 0 warnings

---

## What was built

Replaced the `Task.WhenAny(callTask, timeoutTask)` fire-and-forget timeout pattern in `SpecGenerationService.cs` with proper `CancellationTokenSource.CreateLinkedTokenSource` per-attempt cancellation. Threaded `CancellationToken` through `BedrockService.InvokeAsync` and `InvokeWithImageAsync` so cancellation actually propagates to the AWS SDK call, releasing the Bedrock connection on timeout.

---

## Root Cause Fixed

`Task.WhenAny` with a timeout task does NOT cancel the losing task — `callTask` kept running in the background. With 3 retry attempts, by the 3rd attempt there were 3 concurrent hanging Bedrock SDK calls consuming connections indefinitely.

---

## Files Changed

### `Services/BedrockService.cs`
| Line | Change |
|------|--------|
| 36 | `InvokeAsync` signature: added `CancellationToken cancellationToken = default` |
| 69 | `_client.InvokeModelAsync(request)` → `_client.InvokeModelAsync(request, cancellationToken)` |
| 101 | `InvokeWithImageAsync` signature: added `CancellationToken cancellationToken = default` |
| 109 | Fallback `InvokeAsync(...)` call: added `cancellationToken` as final argument |
| 161 | `_client.InvokeModelAsync(request)` → `_client.InvokeModelAsync(request, cancellationToken)` |

### `Services/SpecGenerationService.cs`
| Lines | Change |
|-------|--------|
| 198–220 | Replaced entire `for` loop body — removed `callTask`, `timeoutTask`, `Task.WhenAny` |
| new 198 | `using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` |
| new 199 | `attemptCts.CancelAfter(TimeSpan.FromSeconds(_specGenConfig.TimeoutSeconds))` |
| new 200–208 | `_bedrock.InvokeWithImageAsync(...)` called with `attemptCts.Token`, wrapped in `try` |
| new 209–210 | `visionSucceeded = true; break;` on success |
| new 211–215 | `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` handles per-attempt timeout; backoff `Task.Delay` passes `cancellationToken` |

---

## Other Callers — No Changes Required

- **`DiscoveryService.cs`** — calls `InvokeAsync` with `CancellationToken.None` (fire-and-forget background task, intentional). Backward compatible with new default parameter. No change.
- **`ArtifactGenerationService.cs`** — calls `InvokeAsync` with positional args only. Backward compatible. No change.

---

## Parallelization

Not applicable — single-file sequential task.

## CC Sessions

1 CC run (Sonnet). Clean on first pass.

---

## Acceptance Criteria Verification

- [x] `BedrockService.InvokeAsync` accepts `CancellationToken cancellationToken = default` and passes it to `_client.InvokeModelAsync` — **verified in file**
- [x] `BedrockService.InvokeWithImageAsync` accepts `CancellationToken cancellationToken = default`, passes it to `_client.InvokeModelAsync` AND to fallback `InvokeAsync` call — **verified in file**
- [x] `SpecGenerationService` vision retry loop uses `CreateLinkedTokenSource(cancellationToken)` + `CancelAfter` — **verified in file**
- [x] No `Task.WhenAny` or `timeoutTask` references remain in vision loop — **verified in file**
- [x] `dotnet build` — 0 errors, 0 warnings — **verified locally**

---

## Known Edge Cases / Things to Scrutinize

- The `catch (Exception ex)` block in `BuildPromptAsync` wrapping the entire `case FileType.Image:` section will catch `OperationCanceledException` when the *overall* CTS fires (linked via `CreateLinkedTokenSource`). This logs a warning and appends `"*Image vision analysis failed — skipped.*"` to the prompt rather than propagating. This was the pre-existing behavior for exception handling in that block — it's acceptable since the overall 10-min timeout catch in `GenerateAsync` handles the full abort path.
- Backoff sleep (`Task.Delay(backoff, cancellationToken)`) passes the overall token, so overall cancellation will also interrupt the sleep between retries — correct behavior.

---

## How to Test Locally

1. Submit a feature request with image mockup files via the NEXUS UI
2. Monitor CloudWatch logs for `[SPEC_GEN] Vision call timed out` — confirm connection count does not grow across retry attempts
3. Verify that overall 10-min CTS cancellation during a vision call propagates correctly (kills the vision call, not just the retry loop)

---

## Cycle 2 — ADO #1812

**Date:** 2026-04-13  
**Engineer:** Tony Stark (software-engineer)  
**Commit:** `210a529`  
**Branch:** main  
**Build:** ✅ 0 errors, 0 warnings

### Reviewer Issues Addressed

**C1 (Critical) — Outer catch must not swallow OperationCanceledException**
- **File:** `Services/SpecGenerationService.cs`, line 238
- Changed `catch (Exception ex)` → `catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)`
- Overall-CTS `OperationCanceledException` now propagates out of the image block and up to the `GenerateAsync` outer handler instead of being silently swallowed with a warning log.

**I1 (Important) — Pass overallCts.Token to primary InvokeAsync call**
- **File:** `Services/SpecGenerationService.cs`, line 83
- Added `overallCts.Token` as 5th argument to `_bedrock.InvokeAsync(...)` call
- Overall 10-minute timeout now also governs the primary spec-gen Bedrock call, not just the vision retry loop.

### Files Changed
- `Services/SpecGenerationService.cs` — 2 surgical line changes (outer catch guard + InvokeAsync token)

### CC Sessions
1 CC run (Sonnet). Clean on first pass.

### Build Verification
`dotnet build` — 0 errors, 0 warnings (confirmed by CC + pre-flight passed)
