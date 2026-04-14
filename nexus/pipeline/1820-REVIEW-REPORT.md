# Review Report — ADO #1820 — Discovery prompt truncation limits

**Date:** 2026-04-14  
**Reviewer:** Hawkeye (code-reviewer)  
**Cycle:** 1  
**Branch:** working tree (uncommitted, based on 7de0146)

---

### Verdict: ✅ PASS

---

## CC Review Summary

CC Sonnet ran adversarial checks against all five focus areas specified in the review brief. All five passed. One nitpick flagged (non-blocking, prompt noise only). No false positives dismissed.

---

## Spec Compliance Check

**Files changed per build report:**
- `Services/Discovery/DiscoveryService.cs` — ✅ modified as specified
- `Services/Discovery/DiscoveryInferenceConfig.cs` — ✅ modified as specified

**Acceptance criteria:**
- [x] Narrative truncation → 1500 chars: ✅ `> 1500` / `[..1500]` confirmed (line ~249)
- [x] Per-file truncation → 8000 chars: ✅ `> 8000` / `[..8000]` confirmed (line ~316)
- [x] Combined 20K cap declared before foreach: ✅ `const int MaxTotalFileChars = 20_000` at line ~295
- [x] MaxTokens = 4096: ✅ confirmed in DiscoveryInferenceConfig.cs line 10

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

| Check | Files | Result |
|---|---|---|
| `MaxTotalFileChars` value | DiscoveryService.cs | ✅ 20_000 |
| `narrativeTruncated` isolation | DiscoveryService.cs | ✅ kbQuery only |
| `totalFileChars` increment sites | DiscoveryService.cs | ✅ text cases only |
| `MaxTokens` default | DiscoveryInferenceConfig.cs | ✅ 4096 |

---

## Issues Found

| Severity | File | Location | Issue | Fix |
|----------|------|----------|-------|-----|
| Nitpick | DiscoveryService.cs | ~line 311 | Cap message `"*[File cap reached — remaining files omitted]*"` repeats once per remaining capped file (not once total). Produces minor prompt noise for submissions with many files past the cap. | Wrap in a `bool capMessageWritten` flag, or move the message outside the switch (written once per loop iteration only on first cap hit). Not a correctness issue. |

---

## Check Details

### Check 1 — Constants (Critical)

All four values verified exact:

```csharp
// Narrative cap — line ~249
var narrativeTruncated = submission.NarrativeText.Length > 1500
    ? submission.NarrativeText[..1500]
    : submission.NarrativeText;

// Per-file cap — line ~316
var content = file.ProcessedText.Length > 8000
    ? file.ProcessedText[..8000] + "\n... [truncated]"
    : file.ProcessedText;

// Combined cap — line ~295
const int MaxTotalFileChars = 20_000;

// MaxTokens — DiscoveryInferenceConfig.cs line 10
public int MaxTokens { get; set; } = 4096;
```

**PASS**

---

### Check 2 — 20K cap logic (Critical)

Statement order in Html/Pdf/Text/Other case verified:

1. `totalFileChars = 0` declared **before** foreach ✅
2. Cap guard fires **before** any content is built ✅
3. `remaining = MaxTotalFileChars - totalFileChars` computed correctly ✅
4. 8000-char per-file truncation applied → `content` finalized ✅
5. `remaining` cap applied to `content` if needed ✅
6. `totalFileChars += content.Length` increments by **actual appended bytes**, after both truncations ✅

No off-by-one. `totalFileChars` never uses `file.ProcessedText.Length` raw.

**PASS**

---

### Check 3 — `break` scope (Critical)

`break` inside switch nested in foreach — exits switch only (correct C# behavior). Foreach continues.

Actual behavior per loop iteration after cap is hit:
1. File header (`### filename`) written — foreach top, before switch ✅
2. Switch entered → cap guard fires → cap message appended → `break` exits switch ✅
3. `userPromptSb.AppendLine()` (blank line separator) executes — inside foreach, outside switch ✅
4. Next file iterates

Headers written, content omitted — design achieved. ✅

*Nitpick: cap message re-emits once per capped file. See Issues table.*

**PASS**

---

### Check 4 — Image files don't affect `totalFileChars` (Important)

`totalFileChars +=` appears exactly once in the entire method — at line ~321 inside the Html/Pdf/Text/Other case.

`case FileType.Image:` block does not reference `totalFileChars`. `default:` case does not reference `totalFileChars`.

**PASS**

---

### Check 5 — Narrative truncation scope (Important)

```csharp
// kbQuery — uses truncated (line ~252)
var kbQuery = $"{submission.Title}. {narrativeTruncated}";

// User prompt — uses full text (line ~282)
userPromptSb.AppendLine(submission.NarrativeText);
```

`narrativeTruncated` appears only at its declaration and the `kbQuery` line. Never used in any `userPromptSb` call.

**PASS**

---

## Positive Observations

- The `remaining` partial-fill logic is correctly sequenced — the last file that crosses the 20K boundary gets partial content up to the cap, rather than being fully omitted. Clean design.
- Tony correctly identified the `break` scope question in his build report pre-emptively. Good self-documentation.
- No overflow risk: all values well within int range, string slicing bounds-safe.

---

## Final Verdict: ✅ PASS — ships as-is

The nitpick (repeated cap message) is documented here for Tony's awareness but does not block. It can be cleaned up opportunistically.
