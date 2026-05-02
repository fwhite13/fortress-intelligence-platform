# Review Report — ADO#2632
## NBAIS WC Template: Remaining Fidelity Issues

**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `dd7052e`  
**Review Cycle:** 1 of 2  
**Date:** 2026-05-01

---

### Verdict: ✅ PASS

---

## CC Review Summary

CC (Claude Code Sonnet) performed full adversarial review of `build-nbais-wc-template.py` against the service code in `src/services/`. No false positives were identified — all CC findings confirmed as correct. CC invocation:

```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
  CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cd /home/fredw/projects/fip
cat /tmp/ado2632-review-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

## Spec Compliance Check

All 9 acceptance criteria from the WI are met:

| # | Criterion | Status |
|---|-----------|--------|
| 1 | Cover header real Word section | ✅ |
| 2 | Cover footer real Word section | ✅ |
| 3 | Logo aspect ratio | ✅ (see Priority 1 analysis) |
| 4 | Prepared For centering | ✅ |
| 5 | Global vertical alignment | ✅ |
| 6 | Global column widths | ✅ |
| 7 | Contact box gap | ✅ |
| 8 | Signature lines full width | ✅ |
| 9 | Callout box left border | ✅ |

---

## Priority 1 — Logo: Static Image vs Runtime Tag ✅ RESOLVED

**Verdict: Static image approach is CORRECT. Tony's fix is valid.**

### Reasoning

The key question was: does the runtime service require `{%stackedLogoBase64}` as a docxtemplater substitution tag in master.docx?

**No. Here's why:**

1. **`assembleNbaisWcTemplateData` (assembleTemplateData.js:200):**
   ```js
   stackedLogoBase64: logos?.stacked ? logos.stacked.toString('base64') : null
   ```
   The service loads `logo_stacked.png` from S3 path `{templatePrefix}/verticals/nbais-wc/logo_stacked.png` — the **same NBAIS logo every time**. There is no per-member, per-request, or per-vertical branching. The logo is always the single NBAIS stacked logo.

2. **Static embed is functionally equivalent.** The runtime would always embed the same bytes from the same S3 path. Embedding them at build time is semantically identical. No functionality is lost.

3. **The static fix solves the actual bug correctly.** The squish was caused by `getSize()` in the image module returning `[150, 75]` (hardcoded 2:1 ratio) for all image tags. The NBAIS logo is 1500×1429px — essentially square. At runtime with the tag, the logo would render at 150×75px and be severely squished. The static image sidesteps `getSize()` entirely and embeds the image at the correct 2.5in × 2.382in (1500:1429 aspect ratio).

   | Method | Rendered size |
   |--------|---------------|
   | `{%stackedLogoBase64}` at runtime | `getSize()` → [150, 75]px ≈ 1.56in × 0.78in — **squished** |
   | Static image in master.docx | 2.5in × 2.382in — **correct** |

4. **No runtime error from unused template data.** `stackedLogoBase64` is still assembled and passed to `doc.render()` at runtime, but since the tag no longer exists in master.docx, docxtemplater silently ignores it. No error occurs.

5. **Fallback preserved.** If `logo_stacked.png` is missing at build time, the script falls back to inserting `{%stackedLogoBase64}` as a text placeholder, so the pipeline degrades gracefully.

---

## Consistency Audit

### Files Cross-Referenced
- `build-nbais-wc-template.py` ↔ `documentRenderer.js` ↔ `assembleTemplateData.js` — ✅ logo handling consistent
- CONTENT_W = 9360 — used uniformly throughout build script for all width calculations
- `meta.json` logos config ↔ `loadNamedLogos()` key access — ✅ `stacked` / `horizontal` keys match

---

## Issues Found: None

All 9 issue fixes verified clean.

### Issue 1/2 — Header/Footer Sections ✅

| Check | Line | Result |
|-------|------|--------|
| `s1.different_first_page_header_footer = True` (main) | 1606 | ✅ |
| `section.different_first_page_header_footer = True` (function) | 556 | ✅ (redundant but harmless) |
| `first_header.is_linked_to_previous = False` | 558 | ✅ |
| `first_footer.is_linked_to_previous = False` | 584 | ✅ |
| No `tbl_rule`/`cfooter` in body | build_cover_page | ✅ removed, not duplicated |
| `build_standard_header(s3)` | 1615 | ✅ |
| `link_header()` on s4–s9 | 1622, 1629, 1636, 1643, 1650, 1657 | ✅ all 6 linked |
| `link_header()` scope | 472–474 | ✅ links header only; footers set independently per section |

### Issue 4 — Meta Table ✅

| Check | Line | Result |
|-------|------|--------|
| `META_TABLE_W = 5400` (< 9360) | 661 | ✅ ~3.75in |
| `WD_TABLE_ALIGNMENT.CENTER` | 668 | ✅ |
| Label alignment LEFT | 682 | ✅ (`lp.alignment = WD_ALIGN_PARAGRAPH.LEFT`) |

### Issue 5 — Global Vertical Alignment ✅

Actual count: **32** `WD_ALIGN_VERTICAL.CENTER` assignments (build report stated 33 — 1-count discrepancy is a narrative artifact, not a code defect).

Spot-checks:
- `build_premium_summary_page()` — header rows (1027), data rows (1039), total row (1086) — all three row types covered ✅
- `add_two_col_rec_table()` — unconditional assignment at line 385 covers ALL cells including empty/placeholder cells ✅
- `build_next_steps_page()` — sig table (1325–1326), contact boxes (1257, 1279), disclosure cell (1196) ✅
- `build_employee_benefits_page()` callout cell (1563) ✅

### Issue 6 — Column Widths ✅

| Table | Width | Line |
|-------|-------|------|
| `add_kv_table` default `label_pct` | 30% | 494 |
| Coverage & Limits (custom, explicit) | 65% label (`cov_label_w`) | 928 |
| Excluded Persons table (`ep_label_w`) | 30% | 1118 |
| Signature table label | 20% (`int(CONTENT_W * 0.20)`) | 1310 |

Coverage & Limits correctly kept at 65% label — justified for long coverage descriptions. All other KV tables now use 30%.

### Issue 7 — Contact Box Gap ✅

| Check | Value | Line |
|-------|-------|------|
| `CONTACT_BOX_W` | `(9360-200)//2 = 4580` twips | 1230 |
| `SPACER_W` | `9360 - 2×4580 = 200` twips | 1231 |
| `set_no_cell_borders(cell_spacer)` | ✅ called | 1271 |
| `set_cell_bg` on spacer | **Not called** — no fill ✅ | — |
| Total width math | `4580 + 200 + 4580 = 9360 = CONTENT_W` | ✅ |

### Issue 9 — Callout Box ✅

Only one callout box exists (`build_employee_benefits_page`, lines 1551–1583). Pages 7 (`build_recommendations_1_page`) and 8 (`build_recommendations_2_page`) contain only coverage checklist tables — no action-oriented content warranting a callout.

Callout border verified:
```python
'left':   {'val': 'single', 'sz': 36, 'color': '1F3864'}  # 4.5pt dark navy ✅
'top':    {'val': 'none', ...}    # suppressed ✅
'bottom': {'val': 'none', ...}    # suppressed ✅
'right':  {'val': 'none', ...}    # suppressed ✅
```

---

## Nitpicks

- **N1:** Build report stated 33 `WD_ALIGN_VERTICAL.CENTER` assignments; actual count is 32. Not a code defect — narrative miscounting only. Not blocking.

---

## Positive Observations

- The fallback to `{%stackedLogoBase64}` text tag when logo file is missing is a graceful degradation that prevents silent failures in the build pipeline.
- `SPACER_W` computed from `CONTENT_W - 2*CONTACT_BOX_W` rather than hardcoded ensures it always absorbs integer rounding without overflow.
- `add_two_col_rec_table()` applies `WD_ALIGN_VERTICAL.CENTER` unconditionally to all cells including empty placeholders — no gap risk when section lists have unequal lengths.
- Double-setting `different_first_page_header_footer = True` (both in the function and in main) is redundant but harmless and makes intent explicit.

---

## What to Fix

Nothing. Code is clean. Ships as-is.
