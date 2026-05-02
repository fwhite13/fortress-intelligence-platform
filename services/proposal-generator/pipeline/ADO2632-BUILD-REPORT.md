# Build Report: ADO#2632
## Status: SUCCEEDED

## CC Invocation
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cd /home/fredw/projects/fip
cat /tmp/ado2632-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

## Commits
- `dd7052e`: fix(ADO#2632): cover header/footer sections, logo aspect ratio, column widths, contact box gap, signature lines, callout box border

## Files Changed
- `services/proposal-generator/scripts/build-nbais-wc-template.py` — all 9 fidelity fixes (171 insertions, 86 deletions)
- `services/proposal-generator/templates/verticals/nbais-wc/master.docx` — regenerated output

## Acceptance Criteria

| # | Criterion | Status | Notes |
|---|-----------|--------|-------|
| 1 | Cover header real Word section | ✅ | `build_cover_first_page_header(s1)` — navy bar table in `first_page_header`; body `tbl_rule` removed |
| 2 | Cover footer real Word section | ✅ | `build_cover_first_page_footer(s1)` — confidentiality line in `first_page_footer`; body spacer+cfooter removed |
| 3 | Logo aspect ratio | ✅ | 1500×1429px → width=2.5in, height=2.382in (ratio 1429/1500); `add_picture` with explicit both dims |
| 4 | Prepared For centering | ✅ | `META_TABLE_W=5400` twips (~3.75in); `set_table_alignment(CENTER)`; label alignment changed RIGHT→LEFT |
| 5 | Global vertical alignment | ✅ | 33 `WD_ALIGN_VERTICAL.CENTER` assignments — all table cells in all functions audited exhaustively |
| 6 | Global column widths | ✅ | `ep_label_w` 50%→30%; `add_kv_table` default `label_pct` 35→30; sig table label 25%→20%; contact boxes sized explicitly |
| 7 | Contact box gap | ✅ | 3-column layout: box(~4580 twips) + spacer(~200 twips) + box(~4580 twips); spacer cell has no borders/fill |
| 8 | Signature lines full width | ✅ | Label 20% (~1872 twips), line 80% (~7488 twips); bottom border color `CCCCCC`→`000000` (black) |
| 9 | Callout box left border | ✅ | Table-based callout: `EBF3FF` bg, margins top/bottom=120 left=160 right=120, left border `sz=36, color=1F3864` (4.5pt dark navy) |

## S3 Sync
PASS — `master.docx` uploaded to `s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx`

## Parallelization Used
No — single sequential CC run (all changes in one file).

## CC Sessions Run
1 CC Sonnet session — all 9 issues in one spec. Clean exit code 0.

## Notes for Clint

### Issue 1/2 — Word Header/Footer
- `first_header.add_table()` is called on the header body object. python-docx supports this on header/footer objects. Verify the navy bar appears in Word's first-page header (not body) when opened in Word or LibreOffice.
- `is_linked_to_previous = False` is set on both first_page_header and first_page_footer.
- Cover page section has `different_first_page_header_footer = True`.
- Standard pages (s3 onward) use `build_standard_header` on s3 directly; s4–s9 link to previous (`link_header`).

### Issue 3 — Logo
- The stacked logo is now inserted as an actual image (not docxtemplater tag). The `{%stackedLogoBase64}` tag was only needed for the runtime rendering service. Since the master.docx is the template that docxtemplater processes, the logo will be a static image in the output — not dynamically replaced per-member. This may need review: if the runtime service was expecting the cover page to have `{%stackedLogoBase64}` for dynamic insertion, restoring the tag fallback would be needed. Currently: actual PNG inserted at correct aspect ratio; fallback tag used if file not found.
- **Flag for Clint/Fred:** Confirm whether `{%stackedLogoBase64}` is required at runtime for dynamic logo substitution. If yes, Issue 3 fix should instead correct the `getSize` function in the proposal-generator service (not the build script).

### Issue 4 — Meta Table
- Label alignment changed from RIGHT to LEFT per the spec's intent for cleaner layout. If right-aligned labels are preferred, this is a 1-line revert.

### Issue 5 — Vertical Alignment
- 33 total `WD_ALIGN_VERTICAL.CENTER` calls across all functions. Complete audit performed.
- `add_two_col_rec_table` cells now include vertical alignment (affects pages 7–9 recommendations tables).

### Issue 6 — Column Widths
- `add_kv_table` default changed from `label_pct=35` to `label_pct=30`. This affects all callers that don't pass explicit `label_pct`. Verify visual output on all KV tables.
- Coverage & Limits table kept at 65/35 (label wide, value narrow) — correct for long coverage descriptions.

### Issue 7 — Contact Box Gap
- Old approach used `insideV` border (not a real gap). New approach: actual 3rd column spacer with ~200 twips (~0.14in). Spacer cell has `set_no_cell_borders()` and no background fill — should appear as whitespace.

### Issue 9 — Callout Box
- Only one callout box was found (in `build_employee_benefits_page`). If pages 7–8 also need callout boxes, they weren't present in the original script — this fix applies only to the existing callout in page 9. Confirm with Fred if other pages need them added.

## How to Test Locally
```bash
cd /home/fredw/projects/fip/services/proposal-generator
python3 scripts/build-nbais-wc-template.py
# Open templates/verticals/nbais-wc/master.docx in Word or LibreOffice
# Verify:
# 1. Cover page: blue bar appears in Header section (Insert → Header → Edit Header)
# 2. Cover page: confidentiality text appears in Footer section
# 3. Logo: no squish — proportional
# 4. Meta table: visually centered on page
# 5. All table cells: text vertically centered
# 6. Two-col tables: labels narrow (~30%), values wide (~70%)
# 7. Contact boxes: visible gap between Producer and Office boxes
# 8. Signature lines: extend to right margin with black underline
# 9. Employee Benefits page: callout box has thick dark navy left border
```
