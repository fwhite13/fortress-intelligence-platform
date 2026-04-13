# Review Report: ADO#1716 — Discovery Polling Timeout + False Error Banner Fix
**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 1
**Commits Reviewed:** `4d09caa` (fix) + `063c80f` (build report)
**Verdict:** PASS

---

## Spec Compliance Check

**Files Modified:**
- `nexus/src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` ✅
- `nexus/src/FortressNexus.Web/Components/Nexus/DiscoveryStep.razor` ✅

**Scope:** No out-of-scope changes. Commit also includes `firm/pipeline/` review report docs (unrelated documents, not code).

**Acceptance Criteria:**
- [x] Both poll loops extended to 60s ✅
- [x] Error banner suppressed when session status is `Pending` ✅
- [x] `@using FortressNexus.Web.Models.Enums` added to `DiscoveryStep.razor` ✅
- [x] No other logic altered ✅

---

## CC Review Summary

CC read all relevant files: `DiscoverySessionStatus.cs`, `DiscoveryService.cs`, both changed components. All checks passed. No false positives identified.

---

## Consistency Audit

**`DiscoverySessionStatus` static class** (`Models/Enums/DiscoverySessionStatus.cs`):
- Namespace: `FortressNexus.Web.Models.Enums` — matches `@using` directive ✅
- Full value set: `Pending`, `QuestionsReady`, `Answered`, `Skipped`, `Failed`, `Superseded`
- `Processing` does NOT exist — confirmed ✅
- `Pending` is set by `InitiateDiscoveryAsync` at session creation (line 54) — correct in-flight status ✅
- `QuestionsReady` and `Failed` are terminal statuses used in poll break — confirmed ✅

---

## Critical Issues: 0

None found.

---

## Important Issues: 0

None found.

---

## Nitpicks: 0

None.

---

## Detailed Findings

### Poll Loops — NewSpecWizard.razor

**Loop 1 (lines ~451–467):**
| Check | Result |
|---|---|
| Deadline | `DateTime.UtcNow.AddSeconds(60)` ✅ |
| Break condition | `Status is QuestionsReady or Failed` ✅ (unchanged) |
| Delay | `await Task.Delay(1000)` ✅ |
| Finally | `_discoveryLoading = false; await InvokeAsync(StateHasChanged)` ✅ |

**Loop 2 (lines ~603–617):**
| Check | Result |
|---|---|
| Deadline | `DateTime.UtcNow.AddSeconds(60)` ✅ |
| Break condition | `Status is QuestionsReady or Failed` ✅ (unchanged) |
| Delay | `await Task.Delay(1000)` ✅ |
| Finally | `_discoveryLoading = false; await InvokeAsync(StateHasChanged)` ✅ |

Both loops updated identically. Break conditions untouched.

### Error Banner Guard — DiscoveryStep.razor

**New condition:**
```razor
else if (Session == null || (Session.Status != DiscoverySessionStatus.Pending && !Session.Questions.Any()))
```

**Truth table:**

| Session State | Banner Shows? | Correct? |
|---|---|---|
| Session null | ✅ Yes | Yes — never loaded |
| Pending, no questions (in-flight or timed-out poll) | ❌ Suppressed | Yes — job may still complete |
| QuestionsReady, questions populated | ❌ Suppressed | Yes — falls through to `@foreach` |
| QuestionsReady, no questions (edge case) | ✅ Yes | Yes — anomalous state, banner is appropriate |
| Failed | ✅ Yes | Yes — job failed |
| Answered/Skipped, no questions | ✅ Yes | Yes — abnormal state |

**Timeout UX:** When the 60s poll expires with status still `Pending`, `_discoveryLoading` becomes false, skeleton hides, banner is suppressed, and an empty question list renders with Continue/Skip buttons available. No false "couldn't generate questions" message. Acceptable — background job may still complete.

### 60s Deadline Reasonableness

Bedrock + KB retrieval averages 20–40s. 60s provides ~50% headroom. Loading skeletons with "Analyzing your submission…" text display throughout the full poll duration — users see active feedback, no frozen UI.

---

## Positive Observations

- Correct reasoning that `Processing` doesn't exist — using `Pending` as the in-flight guard is the right call given the actual enum values.
- Both poll loops updated symmetrically — easy mistake to miss one, Tony got both.
- Banner truth table covers all edge cases correctly, including the `QuestionsReady` + 0 questions anomaly.

---

## Verdict: PASS

All eight review criteria verified. No issues found. Ready to merge.

**Rollback note:** `DiscoveryStep.razor` without the guard reverts to showing the error banner on every poll timeout — the original bug. Do not revert without also reverting the timeout extension if needed.
