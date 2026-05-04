# Review Report — ADO#2728

**Commit:** `fc62a2e`
**File:** `services/proposal-generator/scripts/build-nbais-wc-template.py`
**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-05-04
**Cycle:** 1

---

## Verdict: ✅ PASS

---

## Spec Compliance Check

**Issues from ADO#2728:**

| # | Issue | Fixed? |
|---|-------|--------|
| 1 | Pg 5 classification schedule row height (spacing not applied to late-added paragraphs) | ✅ |
| 2 | Pg 7–9 `[+]` outline icon in navigation pane | ✅ |
| 3 | Empty spacer paragraphs (verify not present) | ✅ Verified — no changes needed |

---

## CC Review Summary

CC analysis performed with `--dangerously-skip-permissions` on the full file. All 5 targeted checks returned clean.

**No false positives. No real issues.**

---

## Consistency Audit

**Files cross-referenced:**
- `set_outline_level()` definition (line 298) ↔ call sites in `add_h3` (line 374) and `add_section_divider` (line 386) — ✅ consistent, both pass `level=9`
- `set_para_spacing_zero()` existing pattern (line 287, used at line 324 in `fix_cell_content`) ↔ new call sites at lines 1113 and 1143 — ✅ same function, same usage pattern
- `fix_cell_content()` loop for data row cells (lines 1099–1103) ↔ post-loop additions in c0 and c5 — ✅ gap correctly identified and patched; cells 1–4 unaffected

**Undocumented dependencies checked:**
- All other `add_h3` / `add_section_divider` call sites (14 total across pages 7–9) inherit the `set_outline_level` fix automatically — no per-call-site changes needed ✅
- All other `fix_cell_content` call sites (header row, total row, banner, kv-table, coverage table) — untouched, unaffected ✅

---

## Issues Found: None

No Critical, Important, or Nitpick issues.

---

## Detailed Findings

### Issue 1 — Classification Schedule Spacing

**Root cause confirmed:** The `fix_cell_content()` loop runs first (lines 1099–1103), zeroing the first paragraph in all 6 data-row cells. After the loop, cells 0 and 5 each call `add_paragraph()` to create a second paragraph — these arrive after `fix_cell_content` and miss its zero-spacing treatment.

**Fix verified:**
- `p0b` (cell 0, `{state}` tag) — `set_para_spacing_zero(p0b)` added ✅
- `p5b` (cell 5, `{/classSchedule}` tag) — `set_para_spacing_zero(p5b)` added ✅

**Are there other uncovered paragraphs?** No. Cells 1–4 use only `paragraphs[0]` (already zeroed). `p0a` and `p5a` (the first paragraphs, which get `{#classSchedule}` and `{classEstPremium}`) were zeroed by `fix_cell_content` before runs were appended — runs don't affect paragraph spacing, so those remain correct. Coverage is complete.

### Issue 2 — Outline Level Helper

**`set_outline_level()` implementation:**
```python
pPr = para._p.get_or_add_pPr()
for existing in pPr.findall(qn('w:outlineLvl')):
    pPr.remove(existing)
outline_lvl = OxmlElement('w:outlineLvl')
outline_lvl.set(qn('w:val'), str(level))
pPr.append(outline_lvl)
```

- ✅ Uses `get_or_add_pPr()` (safe — won't duplicate `<w:pPr>`)
- ✅ Remove-before-append pattern (idempotent — safe to call multiple times)
- ✅ `val=9` is the correct OOXML value for Body Text outline level (suppresses `[+]` in Word navigation pane); levels 0–8 map to Heading 1–9

**Applied to:**
- `add_h3` (line 374) ✅
- `add_section_divider` (line 386) ✅

All heading-style paragraphs on pages 7–9 flow through one of these two helpers. Coverage is complete.

### Issue 3 — Empty Spacer Paragraphs

Verified not present. No code changes. ✅

---

## Scope Check

**Only changed:**
- New `set_outline_level()` helper (10 lines, self-contained)
- Two `set_outline_level(para, 9)` calls in `add_h3` and `add_section_divider`
- Two `set_para_spacing_zero()` calls in `build_coverage_details_continued_page`

No other sections touched. No existing logic modified. 14 lines added, 0 deleted.

---

## Positive Observations

- The remove-before-append pattern for `w:outlineLvl` is correct and idempotent — matches the existing `set_para_spacing_zero` pattern in the codebase.
- Fixing via the helper functions (`add_h3`, `add_section_divider`) rather than per-call-site is the right approach — all 14 call sites get the fix automatically.
- The root cause analysis for the classification schedule was accurate: the late-added paragraphs were precisely identified.

---

_Hawkeye — cycle 1 complete. Ships._
