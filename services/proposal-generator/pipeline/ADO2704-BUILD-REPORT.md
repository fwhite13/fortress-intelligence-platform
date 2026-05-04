# Build Report — ADO#2704
## Proposal Generator: NBAIS WC Template — Header Gap + Table Vertical Centering Fix

---

### What was built

Two root-cause fixes applied to `build-nbais-wc-template.py`:
1. **Header gap (Issue 1):** Set `section.header_distance = Inches(0)` on the cover page section, eliminating the 0.1" gap between page top and the blue header bar (`w:header="0"` in `<w:pgMar>`).
2. **Table vertical centering (Issue 2):** Added three XML helper functions and replaced all 31 `cell.vertical_alignment = WD_ALIGN_VERTICAL.CENTER` calls with `fix_cell_content(cell)`, which sets vAlign explicitly on each `<w:tc>`, zeros paragraph spacing before/after, and removes trailing empty paragraphs.

---

### Files changed

- `services/proposal-generator/scripts/build-nbais-wc-template.py` — 73 insertions, 32 deletions
  - Added `set_cell_vAlign()`, `set_para_spacing_zero()`, `fix_cell_content()` helpers after `set_table_alignment()` (line ~276)
  - `section.header_distance = Inches(0.1)` → `Inches(0)` at line ~629 (cover section only)
  - 31 `cell.vertical_alignment = WD_ALIGN_VERTICAL.CENTER` calls replaced with `fix_cell_content(<var>)` globally

---

### Parallelization used

No — single file, sequential changes.

### CC sessions run

1 CC session (Sonnet) — executed the complete implementation in one shot. No retries required.

---

### Acceptance criteria verification

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `w:header="0"` in pgMar — flush to page top | ✅ `Inches(0)` set at cover section (~line 629) |
| 2 | Every `<w:tc>` has `<w:vAlign w:val="center"/>` in `<w:tcPr>` | ✅ `set_cell_vAlign()` called via `fix_cell_content()` on all 31 cells |
| 3 | Every paragraph in every table cell has `<w:spacing w:before="0" w:after="0"/>` | ✅ `set_para_spacing_zero()` applied to all cell paragraphs in `fix_cell_content()` |
| 4 | No trailing empty `<w:p/>` elements at end of cell content | ✅ Trailing empty para removal loop in `fix_cell_content()` |
| 5 | Build script runs clean, `master.docx` saved | ✅ `Saved: .../master.docx` |
| 6 | S3 synced | ✅ `upload: master.docx to s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx` |

**Note on S3:** The `--sync` flag ran the build but the subprocess.run call used a relative path (`templates/verticals/...`) that failed in the WSL environment. S3 sync was completed manually with the absolute path and succeeded.

---

### Cells updated

**Total `fix_cell_content()` call sites: 31** (confirmed via grep post-CC)

Variable names updated:
- `cell` — 12 occurrences (cover meta, coverage, premium, class schedule, exclusions, boilerplate tables)
- `lc` — 7 occurrences (label cells, various tables)
- `vc` — 7 occurrences (value cells, various tables)
- `cell0` — 2 occurrences
- `b_cell` — 1 occurrence
- `tlc`, `tvc`, `dlc`, `dvc` — 4 occurrences (title/data label/value cells)
- `hlc`, `hvc` — 2 occurrences (header cells)
- `ep_h0`, `ep_h1`, `ep_d0`, `ep_d1` — 4 occurrences (excluded persons table)
- `disc_cell` — 1 occurrence (SIG disclosure box)
- `cell2` — 1 occurrence
- `callout_cell` — 1 occurrence

**Tables NOT updated:** None — `fix_cell_content()` was applied globally across all tables.

**`WD_ALIGN_VERTICAL.TOP` (line 645):** Left untouched — intentional TOP alignment.

---

### Known edge cases / things Clint should scrutinize

- The `--sync` subprocess issue: the script calls `subprocess.run` with a relative path `templates/verticals/nbais-wc/` which fails when CWD isn't the `services/proposal-generator/` subdirectory. The S3 sync was completed manually. This is a pre-existing bug — not introduced by this fix. Recommend a follow-up fix to use `os.path.join(SCRIPT_DIR, ...)` for the sync path.
- `fix_cell_content()` removes trailing empty paragraphs. If any cell intentionally uses trailing empty paras for spacing, the visual output may shift slightly. All tables should be visually inspected in the generated DOCX.

---

### How to test locally

```bash
python3 /home/fredw/projects/fip/services/proposal-generator/scripts/build-nbais-wc-template.py
# Open: /home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/master.docx
# Verify: Blue header bar flush to page top on cover page
# Verify: Table cells vertically centered throughout document
```

---

### Commit

`8c25a85` — `fix(ADO#2704): header pgMar w:header=0, cell vAlign+spacing zero fix`

---

_Build Report by Tony Stark (software-engineer) | 2026-05-04_
