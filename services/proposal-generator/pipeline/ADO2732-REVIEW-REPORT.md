# Review Report — ADO#2732

**WI:** ADO#2732 — NBAIS WC template v2: remove empty paragraphs from docx XML  
**Commit reviewed:** `a64c6ab` (HEAD → main)  
**Review cycle:** 1 of 2  
**Date:** 2026-05-04

---

### Verdict: NEEDS-CHANGES ❌

---

## CC Review Summary

**CC Invocation:**
```bash
cd /home/fredw/projects/fip && cat services/proposal-generator/pipeline/ADO2732-REVIEW-CC-BRIEF.md | \
  claude --model sonnet --print --dangerously-skip-permissions
```

CC returned findings for all 5 fixes — all 5 unverified. Manual Python/lxml verification confirmed every CC finding. Additionally, a cross-commit comparison revealed that `a64c6ab` (HEAD) **reverted** Fix 1 and Fix 4 from the prior commit `ce8a2b5`, making the current XML state worse than it was before the HEAD commit.

---

## Consistency Audit

**Commits inspected:**
- `ce8a2b5` — first attempt: Fix 1 and Fix 4 correctly applied, Fixes 2/3/5 not attempted (tagged as "not found")
- `a64c6ab` — HEAD: all 5 fixes claimed in commit message; file size decreased 423126 → 423073 bytes

**Cross-commit comparison (Python lxml analysis):**
The HEAD commit `a64c6ab` re-introduced leading empty paragraphs to recommendation tables 14, 16, 17, 18, 20, 21, 22 that `ce8a2b5` had correctly cleaned. It also un-consolidated Fix 4 (`{#excludedPersons}` + `{name}`) back to 2 paragraphs. Fixes 2, 3, and 5 remain unapplied in both commits.

---

## Spec Compliance Check

**Build report claims:** All 5 fixes applied, generation test 423KB clean.  
**Actual XML state at HEAD:** None of the 5 fixes are correctly present.

The generation test (Docxtemplater render, 423KB) may have passed because Docxtemplater doesn't error on extra blank paragraphs — they just render as blank lines. The generation success does not validate that the XML edits were applied.

---

## Issues Found

| Severity | Fix | Issue | Evidence |
|----------|-----|-------|----------|
| Critical | Fix 1 | 22 cells across recommendation tables 14, 16, 17, 18, 20, 21, 22 still have leading empty `<w:p>` — and `a64c6ab` actually *re-introduced* them (they were clean in `ce8a2b5`) | Python lxml: `Table 14: 8/8 cells with leading empty para` vs `ce8a2b5: 0/8` |
| Critical | Fix 2 | `{classEstPremium}` and `{/classSchedule}` remain on separate `<w:p>` elements in Table 7 Cell 5 | `classSchedule: '{classEstPremium}{/classSchedule}' → 2 para(s)` in both commits |
| Critical | Fix 3 | `{#classSchedule}` and `{state}` remain on separate `<w:p>` elements in Table 7 Cell 0 | `classSchedule: '{#classSchedule}{state}' → 2 para(s)` in both commits |
| Critical | Fix 4 | `{#excludedPersons}` and `{name}` are back to 2 paragraphs — `ce8a2b5` had this correctly consolidated to 1; `a64c6ab` reverted it | `excludedPersons: '{#excludedPersons}{name}' → 2 para(s)` at HEAD vs `1 para(s)` at `ce8a2b5` |
| Critical | Fix 5 | `Form D-43 — Election to Reject Coverage` and `{/excludedPersons}` remain on separate `<w:p>` elements in Table 8 Cell 1 | `'Form D-43 — Election to Reject Coverage{/excludedPersons}' → 2 para(s)` in both commits |

---

## What Happened

Two commits were made for ADO#2732:

**`ce8a2b5` (correct partial work):**
- ✅ Fix 1: Removed leading empty `<w:p>` from all 22 cells in recommendation tables 14, 16, 17, 18, 20, 21, 22
- ✅ Fix 4: Consolidated `{#excludedPersons}` + `{name}` onto single paragraph
- ❌ Fix 2, 3, 5: Noted as "not found" — not attempted

**`a64c6ab` (HEAD — build report commit, regressed):**
- ❌ Fix 1: Re-introduced leading empty `<w:p>` to all 22 recommendation table cells (regression)
- ❌ Fix 2: `{classEstPremium}` + `{/classSchedule}` still 2 paragraphs
- ❌ Fix 3: `{#classSchedule}` + `{state}` still 2 paragraphs
- ❌ Fix 4: Un-consolidated `{#excludedPersons}` + `{name}` back to 2 paragraphs (regression)
- ❌ Fix 5: `Form D-43` + `{/excludedPersons}` still 2 paragraphs

The commit message for `a64c6ab` claims all 5 fixes applied, but the XML does not reflect this. The build report is inaccurate.

---

## What to Fix

Tony, the HEAD commit regressed `ce8a2b5`'s correct work and still didn't apply fixes 2, 3, 5. Here's exactly what the XML needs:

### Fix 1 (regression — restore ce8a2b5 work, then keep)
In tables 14, 16, 17, 18, 20, 21, 22 — every cell that starts with an empty `<w:p>` (a `<w:p>` containing only `<w:pPr>` with no `<w:r>`) needs that empty paragraph removed. `ce8a2b5` correctly had 0 leading empty paras in all these tables. Restore that state and do not undo it.

### Fix 2
In Table 7 (classification schedule), the cell containing `{classEstPremium}` currently has:
```xml
<w:p>...<w:t>{classEstPremium}</w:t>...</w:p>
<w:p>...<w:t>{/classSchedule}</w:t>...</w:p>
```
Merge the `{/classSchedule}` run into the same `<w:p>` as `{classEstPremium}`. Result: one paragraph containing both runs.

### Fix 3
In Table 7 (classification schedule), the cell containing `{state}` currently has:
```xml
<w:p>...<w:t>{#classSchedule}</w:t>...</w:p>
<w:p>...<w:t>{state}</w:t>...</w:p>
```
Merge the `{state}` run into the same `<w:p>` as `{#classSchedule}`. Result: one paragraph containing both runs.

### Fix 4 (regression — restore ce8a2b5 work)
In Table 8 (excluded persons), the name cell currently has:
```xml
<w:p>...<w:t>{#excludedPersons}</w:t>...</w:p>
<w:p>...<w:t>{name}</w:t>...</w:p>
```
`ce8a2b5` had this as 1 paragraph. Restore: merge `{name}` run into the same `<w:p>` as `{#excludedPersons}`.

### Fix 5
In Table 8 (excluded persons), the election form cell currently has:
```xml
<w:p>...<w:t>Form D-43 — Election to Reject Coverage</w:t>...</w:p>
<w:p>...<w:t>{/excludedPersons}</w:t>...</w:p>
```
Merge `{/excludedPersons}` run into the same `<w:p>` as the Form D-43 text. Result: one paragraph.

---

## Generation Test

The build report shows a clean Docxtemplater render (423KB, no errors). This is noted but does not override the XML findings — Docxtemplater renders blank paragraphs without errors; they just produce blank lines in the output.

---

## Positive Observations

Table 11 (producer contact) is clean — Cell 0 and Cell 2 have no leading empty paragraphs, which is correct. The spacer Cell 1 contains only the mandatory OOXML terminal paragraph, which is not a defect.

---

_Reviewed by Hawkeye (code-reviewer agent). CC invocation used: sonnet, dangerously-skip-permissions._
