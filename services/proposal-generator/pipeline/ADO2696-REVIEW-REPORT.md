# Review Report — ADO#2696 (Cycle 1)

**Commit:** `01a5860` — fix set_cell_width/set_table_width remove-before-append  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-01  
**File reviewed:** `services/proposal-generator/scripts/build-nbais-wc-template.py`

---

### Verdict: PASS ✅

---

### What Was Verified

#### 1. `set_table_width()` — ✅ Fixed (lines 131–139)
Confirmed: removes any existing `w:tblW` from `tblPr` before appending the new element.
```python
for existing in tblPr.findall(qn('w:tblW')):
    tblPr.remove(existing)
```

#### 2. `set_cell_width()` — ✅ Fixed (lines 142–151)
Confirmed: removes any existing `w:tcW` from `tcPr` before appending the new element.
```python
for existing in tcPr.findall(qn('w:tcW')):
    tcPr.remove(existing)
```

---

### Spot Check — Other Helpers

CC flagged the following helpers as having the same append-without-remove pattern:
`set_cell_bg`, `set_para_shading`, `set_cell_margins`, `set_cell_border`, `set_no_cell_borders`, `set_table_borders`, `remove_table_borders`, `set_row_height`, `set_table_alignment`, `set_para_bottom_border`, `set_para_top_border`

**Assessment: Latent debt, not a runtime risk.**  
All flagged helpers are called **once per cell/element** in the script's call graph. Each cell object is freshly created from a new table row — no helper is ever called twice on the same XML element. The bug pattern exists in the code but cannot trigger under current usage.

This is pre-existing code quality debt unrelated to commit `01a5860`. Not blocking. Worth a follow-up cleanup ticket if the script ever evolves to support template re-application or dynamic updates.

---

### Notes
- ADO#2695 was closed as non-blocking; this cycle 1 review tracks under ADO#2696.
- No scope creep from commit `01a5860` — only the two target functions were modified.
- Root cause fix is correct and well-scoped.

---

# Review Report — ADO#2696 (Cycle 2)

**Commit:** `e15148b` — fix(ADO#2696): fix signature table tblGrid to enforce 25/75 column proportions  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-01  
**File reviewed:** `services/proposal-generator/scripts/build-nbais-wc-template.py`

---

### Verdict: NEEDS-CHANGES ⚠️

---

### CC Review Summary

CC analyzed all `set_table_grid()` applications plus any multi-column tables without it. The helper itself is correct; the primary WI target (signature table) is correctly fixed. However, 5 multi-column tables that Tony built explicit column widths for were not given `set_table_grid()` calls, leaving them with the same tblGrid gap the helper was introduced to solve.

---

### Spec Compliance Check

The WI spec stated: fix signature table `tblGrid` to enforce 25/75 proportions.  
✅ Signature table: correctly fixed with `set_table_grid(tbl_sig, [label_w, line_w])` at 25/75 of `CONTENT_W`.  
✅ Tony extended the fix to 3 additional tables (`tbl_meta`, contact boxes, `add_kv_table`), which is positive scope extension.  
⚠️ 5 multi-column tables with explicit per-cell widths were missed (see Issues).

**Spec compliance verdict:** ✅ WI scope met — but incomplete pattern application raises NEEDS-CHANGES.

---

### `set_table_grid()` Helper Verification

**Lines 154–174:**
- ✅ Removes existing `w:tblGrid` before rebuilding (correct guard against duplicates)
- ✅ Inserts new grid via `tblPr.addnext(grid)` — correct XML position per OOXML spec
- ✅ Helper is self-contained and reusable

---

### Math Verification

| Variable | Expected | Actual | ✓ |
|---|---|---|---|
| `CONTENT_W` | 9360 (6.5in × 1440) | 9360 | ✅ |
| `label_w` (sig) | `int(9360 × 0.25)` = 2340 | 2340 | ✅ |
| `line_w` (sig) | `9360 − 2340` = 7020 | 7020 | ✅ |
| Sum (sig) | 9360 | 9360 | ✅ |
| `label_w` (meta) | `int(5400 × 0.35)` = 1890 | 1890 | ✅ |
| `value_w` (meta) | `5400 − 1890` = 3510 | 3510 | ✅ |
| Sum (meta) | 5400 | 5400 | ✅ |
| `CONTACT_BOX_W` | `(9360−200)//2` = 4580 | 4580 | ✅ |
| `SPACER_W` | `9360 − 2×4580` = 200 | 200 | ✅ |
| Sum (contact) | 9360 | 9360 | ✅ |

**Note on meta table:** Proportion is 35/65, not 25/75 — this is intentional (comment confirms narrower table for center-alignment visual). Not an issue.

---

### Issues Found

| Severity | Table / Function | Lines | Issue |
|----------|-----------------|-------|-------|
| Important | `add_two_col_rec_table` | 402–411 | 2-col, `half=4680` per cell, no `set_table_grid` |
| Important | Coverage at a Glance `tbl` | 854–874 | 2-col, 35/65 split, no `set_table_grid` |
| Important | Coverage and Limits `tbl_cov` | 969–998 | 2-col, 65/35 split, no `set_table_grid` |
| Important | Class Schedule `tbl_cs` | 1047–1114 | 6-col, proportional widths, no `set_table_grid` |
| Important | Excluded Persons `tbl_ep` | 1152–1181 | 2-col, 30/70 split, no `set_table_grid` |

---

### What to Fix

For each of the 5 tables below, add a `set_table_grid()` call immediately after `set_table_width()` (or where the column widths are computed), passing the same width values already used in `set_cell_width()` calls.

**1. `add_two_col_rec_table` (~L408):**
```python
half = CONTENT_W // 2
set_table_grid(tbl, [half, half])
```

**2. Coverage at a Glance (~L873):**
```python
label_w = int(CONTENT_W * 0.35)
value_w = CONTENT_W - label_w
set_table_grid(tbl, [label_w, value_w])
```

**3. Coverage and Limits (~L997):**
```python
cov_label_w = int(CONTENT_W * 0.65)
cov_value_w = CONTENT_W - cov_label_w
set_table_grid(tbl_cov, [cov_label_w, cov_value_w])
```

**4. Class Schedule (~L1050 — after `col_ws` is built):**
```python
set_table_grid(tbl_cs, col_ws)
```

**5. Excluded Persons (~L1180):**
```python
ep_label_w = int(CONTENT_W * 0.30)
ep_value_w = CONTENT_W - ep_label_w
set_table_grid(tbl_ep, [ep_label_w, ep_value_w])
```

---

### Positive Observations

- `set_table_grid()` helper design is clean and correct — remove-before-rebuild is the right pattern.
- Math is tight: all 4 applied tables sum exactly to their respective total widths.
- Extension beyond the WI scope (meta table, contact boxes, `add_kv_table`) is good initiative.

---

_Hawkeye — you see what others miss._

---

# Review Report — ADO#2696 (Cycle 3)

**Commit:** `8db3a0a` — fix(ADO#2696): add set_table_grid to remaining 5 tables  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-01  
**File reviewed:** `services/proposal-generator/scripts/build-nbais-wc-template.py`

---

### Verdict: PASS ✅

---

### CC Review Summary

CC confirmed all 5 `set_table_grid()` calls from the Cycle 2 NEEDS-CHANGES verdict are now present, correctly placed immediately after `set_table_width()`, and using the correct width variables.

| # | Table | Lines | Call | ✓ |
|---|-------|-------|------|---|
| 1 | `add_two_col_rec_table` | 404–406 | `set_table_grid(tbl, [half, half])` | ✅ |
| 2 | Coverage at a Glance | 857–858 | `set_table_grid(tbl, [label_w, value_w])` | ✅ |
| 3 | Coverage and Limits `tbl_cov` | 973–974 | `set_table_grid(tbl_cov, [cov_label_w, cov_value_w])` | ✅ |
| 4 | Class Schedule `tbl_cs` | 1052–1053 | `set_table_grid(tbl_cs, col_ws)` | ✅ |
| 5 | Excluded Persons `tbl_ep` | 1158–1159 | `set_table_grid(tbl_ep, [ep_label_w, ep_value_w])` | ✅ |

### Notes
- All calls placed after `set_table_width()` — correct ordering per OOXML spec.
- No regressions detected. No out-of-scope changes.
- ADO#2696 closed.

---

_Hawkeye — you see what others miss._
