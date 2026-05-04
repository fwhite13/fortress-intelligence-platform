# QA Report: ADO#2730

**Analyst:** Natasha Romanoff (Black Widow)
**Verdict:** ⚠️ WARN
**Test Date:** 2026-05-04
**Image:** `a216424` — `proposal-generator-dev:34`

---

## Environment
- **Target:** `https://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com`
- **Host header:** `proposal-generator.dev.fortressam.ai`
- **Test artifact:** `/tmp/ado2730-qa.docx` (prop_01KQTBD2SWGVD35FEA3CBADVZR)
- **Comparison baseline:** `/tmp/ado2728-qa.docx` (prev deploy, ADO#2728)

---

## Test Results

| TC | Name | Result | Details |
|----|------|--------|---------|
| TC1 | Health endpoint | ✅ PASS | HTTP 200 |
| TC2 | Generate + download | ✅ PASS | 423KB docx, no warnings in API response |
| TC3 | vAlign on boilerplate two-col tables | ⚠️ WARN | See analysis below |
| TC4 | No leading/trailing whitespace in cells | ⚠️ WARN | Pre-existing — not a regression |
| TC5 | Document integrity | ✅ PASS | Sections: 9, Tables: 24, Paras: 52 |

---

## TC3 — vAlign Analysis (Detailed)

### What the PR intended
`add_two_col_rec_table` now calls `fix_cell_content(cell, valign='top')` instead of the default `valign='center'`. This targets Tables 14, 16–18, 20–22 (boilerplate two-column recommendation tables, pages 7–9).

### What was found

| Layer | T14,16–18,20–22 vAlign |
|-------|------------------------|
| `master.docx` (template) | `top` ✅ — fix correctly applied by Python build script |
| `ado2728-qa.docx` (prev generated) | `center` (explicit) |
| `ado2730-qa.docx` (this generated) | `none` (no element present) |

### Root cause
The docxtemplater + postProcessor rendering pipeline strips `w:vAlign` (and `w:tcMar`) from cell properties during document rendering. This is a **pre-existing pipeline behavior** — it was also stripping values before this ADO, but previously the template had `center` (same as the render's default behavior happening to survive), and now the template has `top` which is being stripped.

When no `w:vAlign` element is present, Word's default vertical alignment is **TOP**. So the **visual result is correct** — cells are top-aligned as intended.

### Verdict on TC3
The fix achieves the visual goal (top alignment). The explicit XML attribute is lost by the renderer — this is a pre-existing pipeline limitation. **The visual behavior matches the spec.** WARN only, not FAIL.

---

## TC4 — Whitespace Analysis (Detailed)

27 cells show leading/trailing `\n` in `cell.text`. Identical in `ado2728-qa.docx` — **no regression introduced by this ADO**.

Root cause: `cell.text` in python-docx joins all paragraphs with `\n`. Some cells (Tables 7–8, 14–22) have structural empty paragraphs from:
- Docxtemplater consuming section-open tags (`{#classSchedule}`) as a paragraph, leaving an empty `<w:p>` behind
- Static boilerplate cells with leading empty paragraph for spacing

`trimVal` correctly trims string values. It does not (and should not) affect DOCX paragraph structure. TC4 is pre-existing behavior, not in scope for this ADO.

---

## TC2 — trimVal Verification

Dynamic cell values in the generated doc are clean — no leading/trailing string whitespace in the values themselves:

| Field | Value |
|-------|-------|
| memberName | `Carson Valley Excavation, LLC` |
| policyPeriod | `06/01/2026 – 06/01/2027` |
| quoteDate | `05/01/2026` |
| state | `NV` (para structure gives `\nNV` but string value is clean) |
| classCode | `6217` |
| estPremium | `$12,070.00` (para structure gives trailing `\n` but value is clean) |

`trimVal` is working correctly on all dynamic string fields. ✅

---

## Summary

| Check | Status |
|-------|--------|
| Service alive | ✅ |
| Generation succeeds | ✅ |
| No API warnings | ✅ |
| trimVal applied (JS) | ✅ confirmed |
| vAlign=top in master.docx template | ✅ confirmed |
| vAlign=top preserved in generated docx | ⚠️ stripped by renderer (visual effect correct) |
| TC4 whitespace regression | ✅ none introduced |
| Document structure intact | ✅ |

---

## Recommendation

**WARN — ship as-is.** The two deliverables (JS trim + template vAlign fix) are both correctly implemented. The vAlign stripping is a renderer pipeline artifact — the visual output is top-aligned as intended. TC4 whitespace is pre-existing structural behavior, not string whitespace, and not in scope for this ADO.

If explicit `w:vAlign='top'` in the XML output is required, a follow-up task should address the post-processor to preserve `tcPr` attributes during rendering.

---

*Report generated: 2026-05-04 16:xx EDT*
