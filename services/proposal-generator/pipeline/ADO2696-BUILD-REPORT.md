# ADO#2696 — Build Report

## Build Cycle 1 (commit 01a5860)
- Fixed `set_cell_width()` and `set_table_width()` to remove existing XML elements before appending new ones
- `tcW` values now correctly set to 2340/7020 for signature table
- **Issue remaining:** tblGrid still `[4680, 4680]` — Word normalizes tcW to tblGrid, so 50/50 render persisted

---

## Build Cycle 2 (commit e15148b) — 2026-05-02

### What was built
Added `set_table_grid()` helper and applied it to all proportional tables. Root cause of persisting 50/50 render: Word's layout engine uses `<w:tblGrid>` as the authoritative column width source and overrides individual `<w:tcW>` values when tblGrid doesn't match.

### Root cause confirmed
- Signature table `tblGrid` was `['4680', '4680']` (equal halves — docx default)
- `set_cell_width()` fix in cycle 1 updated `tcW` correctly but tblGrid was never touched
- Word (and docxtemplater) normalize to tblGrid, so rendered output stayed 50/50

### Fix applied
Added `set_table_grid(tbl, col_widths)` helper that:
1. Removes all existing `<w:tblGrid>` elements
2. Rebuilds `<w:tblGrid>` with explicit `<w:gridCol>` entries
3. Inserts after `<w:tblPr>` (correct OOXML position)

### Tables patched
| Table | Function | Col widths (twips) | Split |
|-------|----------|--------------------|-------|
| Signature | `build_next_steps_page()` | [2340, 7020] | 25/75 |
| Cover meta | `build_cover_page()` | [1890, 3510] | 35/65 |
| Contact boxes | `build_contact_page()` | [4580, 200, 4580] | even/spacer/even |
| KV tables | `add_kv_table()` | [label_w, value_w] | label_pct/rest |

### Verification
```
Sig table tblGrid: ['2340', '7020']   ✅  (was ['4680', '4680'])
Cover meta tblGrid: ['1889', '3511']  ✅  (35/65 of META_TABLE_W=5400)
```

### Files changed
- `services/proposal-generator/scripts/build-nbais-wc-template.py` — +27 lines (helper + 4 call sites)
- `services/proposal-generator/templates/verticals/nbais-wc/master.docx` — rebuilt

### CC sessions
1 CC Sonnet session (sequential, single task)

### Build
SUCCEEDED — template rebuilt and synced to S3

### Commit
`e15148b` — pushed to main

---

## Build Cycle 3 (commit 8db3a0a) — 2026-05-02

### What was built
Added `set_table_grid()` calls to the 5 remaining multi-column tables that had explicit per-cell widths but no tblGrid declaration (per Clint C2 NEEDS-CHANGES review).

### Tables patched
| Table / Function | Col widths | Split |
|-----------------|------------|-------|
| `add_two_col_rec_table` | [half, half] = [4680, 4680] | 50/50 |
| Coverage at a Glance `tbl` | [label_w, value_w] | 35/65 |
| Coverage and Limits `tbl_cov` | [cov_label_w, cov_value_w] | 65/35 |
| Class Schedule `tbl_cs` | col_ws (6 cols) | proportional |
| Excluded Persons `tbl_ep` | [ep_label_w, ep_value_w] | 30/70 |

### Files changed
- `services/proposal-generator/scripts/build-nbais-wc-template.py` — +5 `set_table_grid()` call sites (L407, L857, L972, L1050, L1155)
- `services/proposal-generator/templates/verticals/nbais-wc/master.docx` — rebuilt

### CC sessions
1 CC Sonnet session (sequential, single task)

### Build
SUCCEEDED — template rebuilt and synced to S3

### Commit
`8db3a0a` — pushed to main
