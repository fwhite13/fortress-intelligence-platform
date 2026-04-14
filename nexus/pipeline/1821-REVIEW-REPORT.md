# Review Report — ADO #1821 — Discovery + SpecGen Large File Handling
**Reviewer:** Hawkeye (code-reviewer)  
**Cycle:** 1  
**Date:** 2026-04-14  
**Commit:** `545622a` (SpecGen PDF case) + `b5beaf2` (DiscoveryService pre-pass, SpecGen Text case, image cap/vision changes)

---

## Verdict: NEEDS-CHANGES

One logic inconsistency: `FileType.Other` files are summarized by the pre-pass in `SpecGenerationService` but the result is never used — the switch case falls through to "unsupported." Fix before ship.

---

## Spec Compliance Check

No developer brief was provided for this task. Review performed against the task description in ADO #1821 as described in the build brief.

**Files modified:**
- `DiscoveryService.cs` — ✅ Task.WhenAll pre-pass, summarize-or-verbatim, image cap 3→5, vision 512→2000
- `SpecGenerationService.cs` — ✅ Task.WhenAll pre-pass, Text + Pdf summarize-or-verbatim

**Spec compliance verdict:** ✅ Compliant (with the one logic defect noted below)

---

## CC Review Summary

CC (Opus) performed full adversarial analysis across all 10 checklist items. 9/10 checks PASSED cleanly. One real finding: `FileType.Other` pre-pass/switch mismatch in SpecGenerationService. Confirmed by direct code inspection. No false positives.

---

## Consistency Audit

| Cross-reference | Finding |
|---|---|
| Pre-pass key (`f.Id`) ↔ switch lookup (`file.Id`) — Discovery | ✅ Match: both `UploadedFile.Id` (int) |
| Pre-pass key (`f.Id`) ↔ switch lookup (`file.Id`) — SpecGen | ✅ Match: both `UploadedFile.Id` (int) |
| CTS `ct` variable in Discovery catch filter | ✅ Correct: `!ct.IsCancellationRequested` |
| CTS `cancellationToken` variable in SpecGen catch filter | ✅ Correct: `!cancellationToken.IsCancellationRequested` |
| Pre-pass file types ↔ switch cases (Discovery) | ✅ Match: Html/Pdf/Text/Other all 4 sides |
| Pre-pass file types ↔ switch cases (SpecGen) | ❌ Mismatch: Other included in pre-pass, not in switch |
| Vision maxTokens (Discovery) | ✅ Hardcoded `2000`, NOT `_specGenConfig.VisionMaxTokens` |
| Vision maxTokens (SpecGen) | ✅ `_specGenConfig.VisionMaxTokens` (8192) — unchanged |
| narrativeTruncated scope (Discovery) | ✅ KB query only, never injected into userPromptSb |

---

## Critical Issues: 0

No critical issues.

---

## Important Issues: 1

### I1: SpecGenerationService — `FileType.Other` summarized but summary never used
- **File:** `SpecGenerationService.cs` lines 150-155 (pre-pass filter) vs lines 327-331 (switch case)
- **Category:** Logic inconsistency
- **Issue:** The pre-pass `textFileIds` filter includes `FileType.Other` (line 152). This means any `Other` file with `ProcessedText.Length > 40K` will fire a Bedrock summarization call. However, the switch case at lines 327-330 is:
  ```csharp
  case FileType.Other:
  default:
      sb.AppendLine("**File Type: Unknown/Unsupported**");
      sb.AppendLine("*[Binary or unsupported file type — content not included]*");
      break;
  ```
  The summary is computed, stored in the `summaries` dict, and silently discarded. Wasted Bedrock inference call.
- **Context:** In ADO #1814, Tony intentionally simplified `FileType.Other` in SpecGen to "unsupported" (it used to render `ProcessedText`). The #1821 pre-pass was added without accounting for this — the pre-pass was likely copied from DiscoveryService where `Other` IS rendered.
- **Impact:** Wasted Bedrock call (10K maxTokens) per large `FileType.Other` file during SpecGen. Silent — no user-visible error, no incorrect output, but burning unnecessary inference tokens.
- **Fix (pick one):**
  - **Option A — Match DiscoveryService:** Render `FileType.Other` content in SpecGen switch alongside `Text/Pdf`. This is the more consistent approach.
  - **Option B — Remove from pre-pass:** Remove `FileType.Other` from the `textFileIds` filter in `BuildPromptAsync` since the switch won't use it.

  Option A is preferred for consistency. The one-line change:
  ```diff
  -                    case FileType.Other:
  -                    default:
  -                        sb.AppendLine("**File Type: Unknown/Unsupported**");
  -                        sb.AppendLine("*[Binary or unsupported file type — content not included]*");
  -                        break;
  +                    case FileType.Other:
  +                        sb.AppendLine("**File Type: Other**");
  +                        if (!string.IsNullOrWhiteSpace(file.ProcessedText))
  +                        {
  +                            string otherContent;
  +                            if (file.ProcessedText.Length > 40_000)
  +                            {
  +                                otherContent = summaries.TryGetValue(file.Id, out var otherSummary) && otherSummary != null
  +                                    ? $"[Summarized — original {file.ProcessedText.Length:N0} chars]\n{otherSummary}"
  +                                    : file.ProcessedText[..40_000] + "\n... [truncated — summarization failed]";
  +                            }
  +                            else
  +                            {
  +                                otherContent = file.ProcessedText;
  +                            }
  +                            sb.AppendLine(otherContent);
  +                        }
  +                        else
  +                        {
  +                            sb.AppendLine("*[File content not available]*");
  +                        }
  +                        break;
  +
  +                    default:
  +                        sb.AppendLine("**File Type: Unknown/Unsupported**");
  +                        sb.AppendLine("*[Binary or unsupported file type — content not included]*");
  +                        break;
  ```

---

## Nitpicks: 0

---

## Checklist Results

| # | Check | Verdict |
|---|-------|---------|
| C1 | Task.WhenAll exception handling — per-task try/catch isolates failures | ✅ PASS |
| C2 | CTS pattern — linked, using var, CancelAfter 120s, OCE guard correct | ✅ PASS |
| C3 | summaries dict keyed by `f.Id` ↔ lookup by `file.Id` — same int field | ✅ PASS |
| C4 | Pre-pass filter matches switch cases (Discovery) | ✅ PASS |
| C5 | SpecGen BuildPromptAsync uses `cancellationToken` (not None) in CTS | ✅ PASS |
| I6 | Zero large files — empty array → empty dict, no throw | ✅ PASS |
| I7 | Image cap is exactly 5, log message updated | ✅ PASS |
| I8 | Discovery vision maxTokens hardcoded `2000` (not VisionMaxTokens) | ✅ PASS |
| I9 | SpecGen image handling unchanged — VisionMaxTokens, no cap | ✅ PASS |
| I10 | narrativeTruncated for KB query only; NarrativeText injected verbatim | ✅ PASS |
| BONUS | SpecGen Other in pre-pass, not in switch | ❌ NEEDS-CHANGES |

---

## Positive Observations

- CTS pattern is textbook — both services correctly link to outer token, use `using var`, and distinguish per-call timeout from overall cancellation. Clean implementation of the #1812 pattern.
- Individual task `try/catch` (preferred pattern) correctly used in both files — individual summarization failures are isolated and degraded gracefully to 40K truncation fallback.
- Key consistency is clean: both pre-pass and lookup use `UploadedFile.Id` (int). No accidental Guid/int mismatch risk.
- narrativeTruncated correctly scoped: full NarrativeText in the prompt, truncated only for the KB vector query where length limits are appropriate.
- Empty-array edge case handled correctly by the LINQ chain.

---

## What to Fix

**Single fix required:**

In `SpecGenerationService.cs`, either:
- Add `FileType.Other` rendering in the switch (matching DiscoveryService), or
- Remove `FileType.Other` from the pre-pass `textFileIds` filter (line 152)

If the intent is for SpecGen to NOT render `Other` file content (the deliberate #1814 decision), go with Option B — remove from pre-pass. That's a 2-word deletion. If the intent is for SpecGen to align with Discovery and render `Other` content, go with Option A.

Clarify the intent from ADO #1814 and implement accordingly. Either fix is correct depending on product intent; the current state (summarize but discard) is wrong in both cases.

---

## Review Report — ADO #1821 — Cycle 2

**Reviewer:** Hawkeye (code-reviewer)
**Cycle:** 2
**Date:** 2026-04-14
**Commit:** `22dbbe4`
**Focus:** One-line fix — remove `FileType.Other` from `BuildPromptAsync` pre-pass filter

---

### Verdict: PASS ✅

The fix is correct and clean. Both checks passed.

---

### CC Review Summary

CC (Sonnet) performed a targeted two-check adversarial review of `SpecGenerationService.cs` at commit `22dbbe4`. Both checks PASSED with no false positives.

---

### Check Results

| # | Check | Lines | Result |
|---|-------|-------|--------|
| C1 | `FileType.Other` absent from pre-pass `.Where(...)` filter | 150–155 | ✅ PASS — only `Html \| Pdf \| Text` present |
| C2 | `case FileType.Other:` still present and intact in switch | 327–331 | ✅ PASS — outputs "Unknown/Unsupported", untouched |

---

### Details

**Check 1 — Pre-pass filter (lines 150–155):**
```csharp
f.FileType == FileType.Html || f.FileType == FileType.Pdf || f.FileType == FileType.Text
```
`FileType.Other` is absent. The wasted Bedrock inference path is eliminated.

**Check 2 — Switch case (lines 327–331):**
```
case FileType.Other:
default:
    **File Type: Unknown/Unsupported**
    *[Binary or unsupported file type — content not included]*
    break;
```
Case is present and untouched. No accidental deletion.

---

### Spec Fidelity

The Cycle 1 NEEDS-CHANGES feedback specified: remove `FileType.Other` from the pre-pass filter (Option B). Tony implemented exactly that. The switch case behavior is unchanged.

---

**PASS. Ships.**
