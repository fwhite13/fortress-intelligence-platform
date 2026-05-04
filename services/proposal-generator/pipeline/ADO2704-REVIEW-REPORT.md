# Review Report — ADO#2704

**Commit:** `8c25a85` — fix(ADO#2704): header pgMar w:header=0, cell vAlign+spacing zero fix  
**File:** `services/proposal-generator/scripts/build-nbais-wc-template.py`  
**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1  
**Date:** 2026-05-04

---

### Verdict: NEEDS-CHANGES

One missed cell (contact spacer) that will inflate row height; one consistency issue with cover header bar using a different pattern than every other cell in the file.

---

### CC Review Summary

CC performed a full adversarial review across all 7 check points:
- All three helper functions (`set_cell_vAlign`, `set_para_spacing_zero`, `fix_cell_content`) are correctly implemented with proper remove-before-append patterns and guards.
- `header_distance = Inches(0)` is correctly applied to the cover section and not overwritten by `apply_standard_margins()`.
- 3-table spot-check passed completely.
- Two real findings confirmed: one missed cell, one consistency issue.

No false positives dismissed.

---

### Spec Compliance Check

No developer brief path provided. Spec compliance verified against WI description:

- ✅ `section.header_distance` changed to `Inches(0)` on cover section
- ✅ Three helper functions added and wired up
- ✅ 31 cells updated with `fix_cell_content()`
- ⚠️ Contact spacer cell (L1347) not updated — 1 cell missed from the "all table cells" scope
- ✅ `WD_ALIGN_VERTICAL.TOP` exception for cover header bar preserved

---

### Consistency Audit

**Files cross-referenced:** Single file change. No cross-file consistency issues.

**Within-file pattern consistency:**
- `fix_cell_content()` is the established pattern for all cell fixups in this commit
- Cover header bar cell (L641–650) uses a divergent manual pattern: `cell.vertical_alignment = WD_ALIGN_VERTICAL.TOP` + manual `space_before`/`space_after` via python-docx property setters
- All 30 other fixed cells use `fix_cell_content()` — this one is the outlier
- Risk: if future code calls `fix_cell_content()` on this cell (e.g., in a refactor), duplicate `w:vAlign` elements will result since the property setter has no deduplication guard

---

### Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| Important | `build-nbais-wc-template.py` | 1347 | Contact spacer cell skipped — no `fix_cell_content()` call. Empty default paragraph retains inherited Normal-style `space_before`/`space_after`, inflating the contact row height. All other cells in `tbl_contact` (L1335, L1357) are fixed. | Add `fix_cell_content(cell_spacer)` after L1349 |
| Nitpick | `build-nbais-wc-template.py` | 641–650 | Cover header bar uses `cell.vertical_alignment = WD_ALIGN_VERTICAL.TOP` (python-docx setter, no dedup) + manual spacing instead of `fix_cell_content(cell, valign='top')`. Functionally equivalent now but diverges from the pattern every other cell uses. No immediate bug. | Replace L645–648 with `fix_cell_content(cell, valign='top')` for consistency |

---

### Critical Issues: 0

---

### Important Issues: 1

#### I1: Contact spacer cell missing `fix_cell_content()` call

- **File:** `build-nbais-wc-template.py` (line 1347–1349)
- **Category:** Correctness / completeness
- **Issue:** The transparent spacer column cell between the two contact boxes in `build_next_steps_page()` / `tbl_contact` is not processed by `fix_cell_content()`. Its empty paragraph retains inherited spacing (`space_before`/`space_after > 0` from Normal style), which can inflate the contact table row height in a way that misaligns the contact boxes.
- **Evidence:**
  ```python
  cell_spacer = tbl_contact.rows[0].cells[1]
  set_cell_width(cell_spacer, SPACER_W)
  set_no_cell_borders(cell_spacer)
  # ← fix_cell_content(cell_spacer) is missing
  ```
  Compare: cell0 at L1335 and cell2 at L1357 both call `fix_cell_content()`.
- **Impact:** Row height may be inflated by the spacer cell's inherited paragraph spacing, potentially causing visual misalignment in the contact section.
- **Fix:**
  ```diff
  set_cell_width(cell_spacer, SPACER_W)
  set_no_cell_borders(cell_spacer)
  + fix_cell_content(cell_spacer)
  ```

---

### Nitpicks: 1

- **N1:** Cover header bar cell (L641–650) uses `cell.vertical_alignment` property setter instead of `fix_cell_content(cell, valign='top')`. Functionally equivalent for this decorative cell. Replacing it would make the pattern uniform and eliminate the risk of duplicate `w:vAlign` if this cell is ever processed again. Not blocking.

---

### Positive Observations

- All three helper functions are clean and well-structured. Remove-before-append pattern is correct on both `set_cell_vAlign` and `set_para_spacing_zero` — this is the right approach.
- `fix_cell_content`'s `len(paras) > 1` guard to preserve at least one paragraph is correct and safe.
- `header_distance = Inches(0)` is applied exactly once on the cover section and is not clobbered by `apply_standard_margins()` (which is only wired to s3–s9). Clean.
- Coverage across 30 of 31 table cells in scope is thorough.

---

### What to Fix (NEEDS-CHANGES)

**Tony, one fix required:**

1. **L1347–1349 (`build_next_steps_page`, contact spacer cell)** — Add `fix_cell_content(cell_spacer)` after `set_no_cell_borders(cell_spacer)`.

The nitpick (cover bar cell pattern) is optional — fix it if you're touching that area anyway, but it won't block.

---

## Cycle 2 Re-check — 2026-05-04

**Commit:** `97653a1`  
**Reviewer:** Hawkeye (Clint Barton)  
**Verdict: PASS**

### I1 Fix Verified
`fix_cell_content(cell_spacer)` confirmed present at lines 1349–1350, immediately after `set_no_cell_borders(cell_spacer)` in `build_next_steps_page()`. Contact spacer cell is now consistently handled.

### N1 Fix Verified
`WD_ALIGN_VERTICAL.TOP` has been fully removed from the file (no usages remain outside import). Cover header bar cell now uses `fix_cell_content` with `valign='top'`, consistent with the rest of the file.

### ADO Comment
Posted comment #772279 on ADO#2704 confirming cycle 2 PASS.

**Ship it.**
