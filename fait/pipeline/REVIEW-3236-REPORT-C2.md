# Review Report — ADO#3236 (Cycle 2)

### Verdict: NEEDS-CHANGES

---

## CC Review Summary

CC reviewed `AppDbContext.cs`, `FeedbackModal.razor`, and `FeedbackDispatcher.cs` against all 7 cycle-1 fix items. Six of seven verified clean. One issue confirmed: hardcoded fallback URL in `FeedbackDispatcher.cs:34`.

CC finding on I2 confirmed via direct grep — real issue, not a false positive.

Property count discrepancy: spec said 13 HasColumnName mappings; FeedbackSubmission only has 11 properties. CC listed all 11 — all present with correct snake_case. Count in task was off, code is correct.

---

## Consistency Audit

Files cross-referenced:
- `AppDbContext.cs` ↔ `FeedbackSubmission.cs` (entity model) — ✅ column mappings match all 11 properties
- `FeedbackModal.razor` ↔ `FeedbackDispatcher.cs` — ✅ call chain correct
- `FeedbackDispatcher.cs` ↔ `Program.cs` — ✅ registered as scoped service

---

## Cycle 2 Verification Results

| Item | Status | Notes |
|------|--------|-------|
| C1 — OnModelCreating block | ✅ VERIFIED | 11 props, all HasColumnName snake_case; no ValueGeneratedOnAdd on Status; HasDefaultValue("pending") ✅ |
| C2 — Direct DB injection, no loopback | ✅ VERIFIED | IDbContextFactory injected; DbFactory.CreateDbContextAsync() used; no IHttpClientFactory in modal |
| I1 — Hub connection removed, v1 comment | ✅ VERIFIED | No SignalR in modal; v1 upgrade comment at lines 76–77 |
| I2 — Callback URL from config | ❌ FAILED | Hardcoded fallback present — see Critical #1 |
| I3 — InternalToken not in payload | ✅ VERIFIED | Token used only in Authorization header, not in JSON body |
| I4 — FeedbackDispatcher proper DI service | ✅ VERIFIED | Non-static, correct ctor injection, uses IHttpClientFactory.CreateClient(), registered as Scoped in Program.cs:118 |
| I6 — Bare catch fixed | ✅ VERIFIED | Both modal and dispatcher have catch (Exception ex) + LogError calls |

---

## Critical Issues [0]

None.

---

## Important Issues [1]

### I1: Hardcoded fallback URL in FeedbackDispatcher

- **File:** `src/FortressAI.Web/Services/FeedbackDispatcher.cs` (line 34)
- **Category:** Correctness / Config hygiene
- **Issue:** Null-coalescing fallback hardcodes `https://fait.fortressam.ai`. If `FIP:FaitBaseUrl` is missing from config (e.g., local dev, staging), the dispatcher silently sends the callback URL pointing at production.
- **Evidence:**
  ```csharp
  var baseUrl = _config["FIP:FaitBaseUrl"]?.TrimEnd('/') ?? "https://fait.fortressam.ai";
  ```
- **Impact:** In any environment where `FIP:FaitBaseUrl` is not set, Jarvis will attempt to POST status callbacks to the prod FAIT instance — incorrect behavior, potential cross-environment contamination.
- **Fix:**
  ```diff
  - var baseUrl = _config["FIP:FaitBaseUrl"]?.TrimEnd('/') ?? "https://fait.fortressam.ai";
  + var baseUrl = _config["FIP:FaitBaseUrl"]?.TrimEnd('/');
  + if (string.IsNullOrEmpty(baseUrl))
  + {
  +     _logger.LogWarning("[feedback] FIP:FaitBaseUrl not configured — callback URL omitted from Jarvis message");
  +     // omit callback line from payload or return early from dispatch
  + }
  ```
  Alternatively, if a missing base URL should be a hard failure: `throw new InvalidOperationException("FIP:FaitBaseUrl is required")`.

---

## Nitpicks [0]

None.

---

## Spec Fidelity

All cycle-1 issues resolved except I2. The one remaining issue is a direct regression of the original I2 finding — partial fix applied (config key is correct), but the hardcoded fallback defeats the purpose.

---

## What Tony Needs to Fix

**`src/FortressAI.Web/Services/FeedbackDispatcher.cs`, line 34:**

Remove the `?? "https://fait.fortressam.ai"` fallback. Options:
1. Log a warning and conditionally omit the callback URL line from the Jarvis payload if baseUrl is null/empty
2. Throw an `InvalidOperationException` if `FIP:FaitBaseUrl` is not configured (preferred — config errors should be loud)

One line change. Re-submit for cycle 3 spot-check on this file only.

---

_Reviewed by Hawkeye — Cycle 2 — 2026-05-10_
