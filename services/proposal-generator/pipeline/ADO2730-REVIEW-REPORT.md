# Review Report — ADO#2730

**Commit:** `a216424`
**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-05-04
**Cycle:** 1

---

### Verdict: ✅ PASS

---

### CC Review Summary

CC (Sonnet) ran a full adversarial read of both changed files against all checklist items. Zero false positives. Two cosmetic observations noted below as nitpicks — neither blocks.

---

### Spec Compliance Check

**Files touched by commit:**
- `services/proposal-generator/src/services/assembleTemplateData.js` ✅
- `services/proposal-generator/scripts/build-nbais-wc-template.py` ✅
- `services/proposal-generator/templates/verticals/nbais-wc/master.docx` ✅ (binary — template artifact, expected)

No out-of-scope changes. The generic `assembleTemplateData` function is untouched.

**Fix 3A+3B (empty paras, no `para.style` assignments):** Tony verified clean pre-commit — no changes needed, consistent with commit diff showing no deletions in that area.

---

### Consistency Audit

| Cross-check | Result |
|---|---|
| `fix_cell_content` signature supports `valign` param | ✅ Line 308: `def fix_cell_content(cell, valign='center')` |
| All existing callers without `valign` arg unaffected | ✅ Default `'center'` preserved backward compatibility |
| All 7 `add_two_col_rec_table` calls on pages 7–9 route through patched function | ✅ Confirmed via grep + page-section mapping |
| `trimVal` helper scoped to `assembleNbaisWcTemplateData` only | ✅ Not leaked to generic `assembleTemplateData` |

---

### Issues Found

| Severity | File | Location | Issue | Decision |
|---|---|---|---|---|
| Nitpick | `build-nbais-wc-template.py` | L1633 | `callout_cell` calls `fix_cell_content(callout_cell)` without `valign='top'` — single-cell content-driven callout box on page 9; height is text-driven so top vs center is invisible. Technically inconsistent. | Not blocking — cosmetically irrelevant |
| Nitpick | `assembleTemplateData.js` | L150,152 | `classSchedule.estAnnualPayroll` and `classSchedule.classEstPremium` skip `trimVal` and pass through `formatCurrencyWc` only. `formatCurrencyWc` returns `''`, `'$X,XXX.XX'`, or numeric string — no whitespace possible. | Not blocking — no functional risk |

---

### Fix 1 — `trimVal` Coverage (JS)

**classSchedule string fields:**
| Field | trimVal |
|---|---|
| `state` | ✅ |
| `classCode` | ✅ |
| `classDescription` | ✅ |
| `rate` | ✅ |
| `estAnnualPayroll` | ⚠️ formatCurrencyWc only (no whitespace risk) |
| `classEstPremium` | ⚠️ formatCurrencyWc only (no whitespace risk) |

**excludedPersons:** ✅ Handles both string and object shape correctly.

**Top-level return object (all 15 string fields):** ✅ All wrapped.

**Logo base64:** ✅ Correctly excluded — intentional and safe.

---

### Fix 2 — `add_two_col_rec_table` vAlign=top (Python)

- `fix_cell_content(cell, valign='top')` is inside the loop over both columns — every cell in every invocation receives `valign='top'`. ✅
- Pages 7, 8, 9 all exclusively use `add_two_col_rec_table` for their two-column layout tables — all 7 call sites covered by the single patched function. ✅
- No unlisted two-column table builders on pages 7–9 exist. ✅

---

### Regression Check

- Generic `assembleTemplateData` untouched — no regression risk.
- `fix_cell_content` default arg preserves all 20+ existing callers.
- Premium calculations and `formatCurrencyWc` unchanged.
- No behavioral changes outside the targeted fixes.

**No regressions identified.**

---

### Positive Observations

- `trimVal` helper is clean and minimal — correctly guards against non-string input (pass-through for non-strings).
- Backward-compatible parameter addition to `fix_cell_content` is the right pattern — no shotgun refactor.
- Fix 3A+3B correctly identified as "already clean" — no unnecessary noise commits.

---

_Hawkeye — you see what others miss._
