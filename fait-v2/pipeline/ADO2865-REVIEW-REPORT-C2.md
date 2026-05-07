# Review Report — ADO#2865 — Google Stitch Design Agent (Cycle 2)

**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 2 of 2 (requested)
**Date:** 2026-05-07
**Branch:** `main` | **C2 Fixes Commit:** `3ca547d`

---

## Verdict: NEEDS-CHANGES

All 4 Cycle 1 critical/important fixes are verified. However, both of Tony's flagged items expose a **real defect** with a shared root cause: Refine artifacts are persisted with orphaned session IDs that were never written to `DesignAgentSessions`. This is a one-line fix in `DesignAgentView.razor` but it must be present before this ships.

---

## CC Review Summary

CC read all 6 files (wwwroot/js/app.js, Components/App.razor, Services/IDesignAgentService.cs, Services/DesignAgentService.cs, Components/Agent/DesignAgentView.razor, Components/Agent/DesignArtifactCard.razor) and verified each fix point against actual code. CC confirmed the 4 C1 fixes as verified, identified the session continuity defect in the flagged items, found minor diagnostic and CSS observations.

---

## Spec Compliance Check

**Spec:** `memory/projects/fait-v2-spec-2026-04-27.md` §6.0, §6.3

**§2 Codebase Map — Files changed in C2:**
- `wwwroot/js/app.js` — ✅ created
- `Components/App.razor` — ✅ modified (script tag added)
- `Services/IDesignAgentService.cs` — ✅ modified
- `Services/DesignAgentService.cs` — ✅ modified
- `Components/Agent/DesignAgentView.razor` — ✅ modified

**§6 Out of Scope:** ✅ No out-of-scope files touched.

**Spec compliance verdict:** ✅ COMPLIANT on scope — blocked only by session continuity correctness issue below.

---

## Cycle 1 Issue Verification

### C1-Fix 1: `downloadBase64` JS function — ✅ VERIFIED

- `wwwroot/js/app.js` defines `window.downloadBase64 = function (fileName, mimeType, base64String)` — 3 params, correct order.
- `DesignArtifactCard.razor` line 168: `InvokeVoidAsync("downloadBase64", fileName, "text/html", base64)` — argument order matches exactly.
- `Components/App.razor` line 18: `<script src="/js/app.js"></script>` — present, before `</body>` at line 20.

**Status: FIXED. Runtime crash on download eliminated.**

---

### C1-Fix 2: DB persistence in `DesignAgentService` — ✅ VERIFIED (with caveat noted below)

- Constructor: `IDbContextFactory<FaitV2DbContext>` injected and assigned to `_dbFactory`. ✅
- `GenerateScreenAsync`: `DesignAgentSession` created and `SaveChangesAsync` called **before** Stitch API call. ✅
- `SaveArtifactAsync`: `PutObjectAsync` (S3) completes first, then DB write — correct ordering. ✅
- `IDesignAgentService.cs` line 26: `SessionId` optional field on `DesignAgentResult`. ✅
- `DesignAgentView.razor` uses `result.SessionId ?? _currentSessionId` for artifact save. ✅ (shape is correct; continuity gap flagged below)
- DbContext disposal: `await using` pattern used correctly — no leak. ✅

**Status: FIXED. DB writes are wired. Caveat is in Flagged Item 1/2 below.**

---

### C1-Fix 3: `IsStitchAvailableAsync` no longer fake — ✅ VERIFIED

`DesignAgentService.cs` lines 211–215:
```csharp
public Task<bool> IsStitchAvailableAsync(CancellationToken ct = default)
{
    var configured = _config["Stitch:GcpCredentialsConfigured"];
    return Task.FromResult(string.Equals(configured, "true", StringComparison.OrdinalIgnoreCase));
}
```
- Reads correct config key `Stitch:GcpCredentialsConfigured`. ✅
- No fake HTTP call, no hardcoded `true`. ✅
- Case-insensitive comparison is a bonus. ✅

**Status: FIXED.**

---

### C1-Fix 4: Silent catch in `SendPrompt` — ✅ VERIFIED

- `DesignAgentView.razor` line 429: `[Inject] private ILogger<DesignAgentView> Logger { get; set; } = default!;` ✅
- Lines 496–499:
```csharp
catch (Exception ex)
{
    Logger.LogError(ex, "SendPrompt failed for userId={UserId}", _userId);
    _turns.Add(new DesignTurn("assistant", "Something went wrong. Please try again.", null, string.Empty));
}
```
LogError with ex + userId context. No remaining silent catches. ✅

**Status: FIXED.**

---

## Tony's Flagged Items

### Flagged 1: `RefineScreenAsync` session persistence — ❌ BROKEN

**Finding:** `RefineScreenAsync` (`DesignAgentService.cs` lines 131–167) returns `SessionId: null` on all three return paths. It does not create a new session row and does not pass a SessionId out.

In `DesignAgentView.razor` the fallback is:
```csharp
// line 491
var sessionId = result.SessionId ?? _currentSessionId;
```

But `_currentSessionId` is initialized once at component creation:
```csharp
// line 440
private string _currentSessionId = Guid.NewGuid().ToString();
```
**And is never updated.** There is no `_currentSessionId = result.SessionId ?? _currentSessionId;` assignment anywhere.

**Result:**

| Call | `result.SessionId` | `sessionId` used for `SaveArtifactAsync` | Session in DB? |
|---|---|---|---|
| Generate | `"abc-123"` (persisted) | `"abc-123"` | ✅ Yes |
| Refine | `null` | Initial random GUID | **❌ No** |

Refine artifacts are saved with a `SessionId` that was never inserted into `DesignAgentSessions`. If there's a FK constraint this throws on every Refine. If there isn't, all Refine artifacts are orphaned and unqueryable by session.

**Note on "first call is Refine" edge case:** Structurally impossible. `DesignAgentView.razor` lines 477–484 only reach `RefineScreenAsync` when `lastScreenId != null`, which requires a prior successful Generate with a non-null `ScreenId`. This edge case cannot occur naturally. Not a concern.

**The real broken scenario:** Any Refine after a successful Generate. `_currentSessionId` will be the initial random GUID, not the Generate session GUID.

---

### Flagged 2: `SessionId` propagation through `DesignAgentResult` — ❌ BROKEN (same root cause)

- `GenerateScreenAsync`: Returns `SessionId: sessionId` (the persisted GUID). ✅
- `RefineScreenAsync`: Returns `SessionId: null` on all paths. This is correct per spec — Refine reuses the existing session. The problem is that the caller (`DesignAgentView`) must update `_currentSessionId` after a Generate so that subsequent Refine calls have the right session to fall back to.
- `DesignAgentView` never does this assignment.

**Spec §6.3 semantics:** Refine = same session as Generate is correct intent. But the fallback mechanism is broken because the fallback value is never updated.

---

## Issues Found

### Critical Issues [1]

#### C1: `_currentSessionId` never updated after Generate
- **File:** `Components/Agent/DesignAgentView.razor` (line 440 / around line 491)
- **Category:** correctness / data integrity
- **Issue:** `_currentSessionId` is initialized to a random GUID at component creation and never updated. After `GenerateScreenAsync` returns `result.SessionId`, the view should assign `_currentSessionId = result.SessionId` so that subsequent Refine calls reference the correct, persisted session.
- **Impact:** All Refine artifacts written with an orphaned `SessionId`. FK violation if constraint exists; silent data corruption if it doesn't.
- **Fix:**
```diff
// After the artifact save call, around line 491-492:
  var sessionId = result.SessionId ?? _currentSessionId;
+ if (result.SessionId != null)
+     _currentSessionId = result.SessionId;
  await DesignAgent.SaveArtifactAsync(_userId, sessionId, result.Html, artifactName, result.ScreenId, result.IsFallback);
```
*(Or equivalently, add `_currentSessionId = result.SessionId ?? _currentSessionId;` before the artifact call — one line, same effect.)*

---

### Important Issues [0]

None.

---

### Nitpicks [2]

**N1: DB write errors surface generically** — If `SaveChangesAsync` throws in `GenerateScreenAsync` or `SaveArtifactAsync`, it propagates to the outer `catch (Exception ex)` which logs "SendPrompt failed" with no signal that it was a DB failure. Consider a service-level try/catch around DB writes with a more specific log message. Not blocking.

**N2: Hardcoded `px` values in CSS** — `DesignAgentView.razor` and `DesignArtifactCard.razor` have icon sizing (`14px`, `16px`, `18px`, `48px`), button sizing (`36px`), and layout dimensions (`380px`, `200px`, `400px`) as hardcoded values. The `!important` overrides on `font-size` for MudIcon are a known MudBlazor pattern and are consistent with the rest of the codebase — low priority. The layout dimensions (`380px` preview panel, `200px`/`400px` card heights) are candidates for CSS variable extraction if design system sizing tokens are defined. Not blocking; call it when the design system is formalized.

---

### Positive Observations

- JS download helper is clean and correct — blob URL approach with proper cleanup (`URL.revokeObjectURL`).
- `IsStitchAvailableAsync` fix is better than the original spec called for — case-insensitive compare is a nice touch.
- `await using` pattern on `IDbContextFactory` is correct throughout — no context leaks.
- S3-before-DB ordering in `SaveArtifactAsync` is correct and intentional — good.

---

## What Tony Needs to Fix (Cycle 3)

One fix required, one line:

**`Components/Agent/DesignAgentView.razor`** — After `GenerateScreenAsync` returns, assign `_currentSessionId` from `result.SessionId`:

```csharp
// Around line 491 — before or after the sessionId local variable assignment:
_currentSessionId = result.SessionId ?? _currentSessionId;
var sessionId = result.SessionId ?? _currentSessionId;
await DesignAgent.SaveArtifactAsync(_userId, sessionId, result.Html, artifactName, result.ScreenId, result.IsFallback);
```

This ensures all subsequent Refine calls in the same component instance reference the correct, DB-persisted session.

No other changes required. The 4 C1 fixes are solid.

---

## Acceptance Criteria Verification

_(Based on §6.3 of the spec as reflected in the C1/C2 brief)_

- [x] `downloadBase64` JS function defined and wired — ✅ Verified
- [x] DB persistence: session before generate, artifact after S3 — ✅ Verified
- [x] `IsStitchAvailableAsync` is a real check — ✅ Verified
- [x] SendPrompt errors logged — ✅ Verified
- [ ] Refine artifacts correctly linked to Generate session — ❌ NOT MET (C1 above)
