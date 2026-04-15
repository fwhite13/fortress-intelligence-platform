# Review Report — ADO #1805 (Fix Cycle 2)

**Reviewer:** Hawkeye (Clint Barton / code-reviewer)
**Date:** 2026-04-15
**Commit:** `3f9a4c6`
**File reviewed:** `nexus/src/FortressNexus.Web/Services/BedrockService.cs`

---

### Verdict: ✅ PASS

---

## CC Review Summary

CC Sonnet ran adversarial review against the full file. All 7 checks passed. Two low-severity nitpicks surfaced (600s SDK timeout == overall CTS duration edge case; DI registration not visible in file). Both were independently verified and confirmed non-issues. No false positives.

---

## Spec Compliance Check

**Three changes specified in the build brief:**

| # | Change | Status |
|---|---|---|
| 1 | `anthropic_beta` JsonArray added to `InvokeWithImageAsync` requestObj | ✅ Present at line 140 |
| 2 | `Timeout = TimeSpan.FromSeconds(600)` on `AmazonBedrockRuntimeClient` | ✅ Present at line 25 |
| 3 | `OperationCanceledException` logs `reason=caller-cancelled` / `reason=per-attempt-timeout` | ✅ Present at lines 199–200 |

**Out-of-scope check:** `InvokeAsync` text-only path — ✅ NOT modified. No `anthropic_beta` in that method.

**Diff completeness:** 7 insertions, 4 deletions across exactly 3 hunks. No extraneous changes.

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

| Check | Result |
|---|---|
| `anthropic_beta` NOT in `InvokeAsync` requestObj (lines 45–58) | ✅ Confirmed absent |
| `anthropic_beta` value type: `JsonArray` not plain string | ✅ Confirmed |
| `anthropic_beta` string value: `"output-128k-2025-02-19"` exact | ✅ Confirmed |
| Field order: `anthropic_version` → `anthropic_beta` → `max_tokens` | ✅ Confirmed |
| `cancellationToken` in OCE catch references outer method param | ✅ Confirmed — line 104 |

---

## Critical Issues — 0

None found.

---

## Important Issues — 0

None found.

---

## Nitpicks — 2

**N1: SDK timeout (600s) equals overall CTS duration (10 min)**
- `Timeout = TimeSpan.FromSeconds(600)` and the overall spec-gen CTS are both 10 minutes
- In the extreme edge case, both could fire near-simultaneously, making log attribution slightly ambiguous
- In practice, the per-attempt CTS at 120s always fires first — this is not a real operational risk
- Could increase to 620s to make the SDK-as-backstop semantically unambiguous
- **Not blocking.** Current value is correct.

**N2: DI registration concern (self-resolved)**
- CC flagged: if `BedrockService` is transient, a new `AmazonBedrockRuntimeClient` is created per call — expensive and wrong
- **Verified:** `Program.cs` line 137 — `builder.Services.AddSingleton<BedrockService>()` ✅
- Single client instance, shared across all calls. Correct pattern. Non-issue.

---

## Acceptance Criteria Verification

- [x] `InvokeWithImageAsync` requestObj includes `"anthropic_beta": ["output-128k-2025-02-19"]` — ✅ line 140, JsonArray type, exact string value
- [x] `AmazonBedrockRuntimeClient` constructor includes `Timeout = TimeSpan.FromSeconds(600)` — ✅ line 25
- [x] `OperationCanceledException` catch logs `reason=per-attempt-timeout` or `reason=caller-cancelled` — ✅ lines 199–200, logic NOT inverted
- [x] `InvokeAsync` NOT modified — ✅ verified, no `anthropic_beta` in that method
- [x] Build: **0 errors, 0 warnings** — ✅ confirmed via `dotnet build`

---

## Reason Logic Verification (Key Check)

```csharp
// Line 199
var reason = cancellationToken.IsCancellationRequested ? "caller-cancelled" : "per-attempt-timeout";
```

`cancellationToken` = outer method parameter (line 104). Logic trace:
- Outer token **IS** cancelled → caller explicitly stopped the operation → `"caller-cancelled"` ✅
- Outer token **NOT** cancelled → per-attempt CTS fired → `"per-attempt-timeout"` ✅

**Logic is correct. Not inverted.**

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.94
```

---

## Positive Observations

- Clean surgical diff — exactly 3 changes, no scope creep, no noise
- `anthropic_beta` correctly scoped to vision path only — text-only path correctly left untouched
- Logging improvement is high-value for future incident triage; `oce.CancellationToken.GetHashCode()` gives correlation to the CTS that fired
- `IDisposable` pattern for `_client` is correct; singleton registration ensures one client for the lifetime of the app
- Fall-through to `InvokeAsync` on empty imageBytes is a safe, appropriate degradation

---

_Hawkeye — cycle 2 review complete. Root-cause fix verified. Ships clean._
