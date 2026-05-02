# Review Report: ADO#2631
## Verdict: NEEDS-CHANGES

**1 Important issue found.** All other checks pass — tags correct, cover letter verbatim, boilerplate exact, row headers on all 4 tables, banner delegation working. The issue is a single missing vertical alignment on the Excluded Persons loop row.

---

## CC Invocation

```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 && cd /home/fredw/projects/fip && cat /tmp/ado2631-review-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC exited with code 0. Full output synthesized below.

---

## Issues Found

### Critical
None.

### Important

#### I1: Excluded Persons loop row — missing vertical alignment
- **File:** `services/proposal-generator/scripts/build-nbais-wc-template.py`
- **Location:** `build_coverage_details_continued_page()`, lines 1098–1103 (ep_d0, ep_d1 cell setup)
- **Issue:** The `{#excludedPersons}` loop row cells (`ep_d0`, `ep_d1`) never have `cell.vertical_alignment = WD_ALIGN_VERTICAL.CENTER` set. Every other data table in the script applies this rule consistently — Coverage at a Glance, Coverage and Limits, Employee Classification Schedule all have it. The Excluded Persons row is the only data row that will default to top-alignment.
- **Impact:** If an excluded person's name or the "Form D-43 — Election to Reject Coverage" text wraps to two lines, the cell content will be top-aligned instead of vertically centered — visually inconsistent with all other data tables.
- **Fix:**
  ```diff
  ep_d0 = ep_dr.cells[0]
  ep_d1 = ep_dr.cells[1]
  set_cell_width(ep_d0, ep_label_w)
  set_cell_width(ep_d1, ep_value_w)
  set_cell_margins(ep_d0, top=60, bottom=60, left=80, right=60)
  set_cell_margins(ep_d1, top=60, bottom=60, left=80, right=60)
  + ep_d0.vertical_alignment = WD_ALIGN_VERTICAL.CENTER
  + ep_d1.vertical_alignment = WD_ALIGN_VERTICAL.CENTER
  ```

### Nitpick

#### N1: `<h3>&nbsp;</h3>` spacer headings in right columns not reproduced
- **File:** `build-nbais-wc-template.py` — `build_recommendations_2_page()`, `build_employee_benefits_page()`
- **Issue:** The HTML source uses `<h3>&nbsp;</h3>` in right-column cells (Personal Lines, Bond, Life Department, Retirement) to vertically align the first bullet item with the left column. The Python `add_two_col_rec_table()` skips the H3 entirely when `title == ''`, removing this ~12pt top gap. Right-column bullets will appear slightly higher than left-column bullets.
- **Severity:** Nitpick — content correct, minor visual difference. Not blocking.

---

## Edge Case Assessment

### a. `add_banner_continued()` height consistency — PASS
`add_banner_continued()` delegates directly to `add_banner(doc, text, font_size=font_size)`. No independent logic. Both continued and primary banners inherit the 400-twip exact row height, navy background, and white bold text from the same function. Dimensions will be identical.

### b. Page break placement (Premium Summary → Coverage Details) — PASS
Correct python-docx API usage: `WD_BREAK.PAGE` added as a run to a dedicated paragraph with `space_before=Pt(0)` and `space_after=Pt(0)`. The zero-spacing minimizes the risk of producing an extra blank page. The page break and the Coverage Details banner are in the same Word section (s4), which is correct.

### c. SIG Disclosure box borders — PASS
`set_no_cell_borders()` is **not** called on `disc_cell`. Only `set_cell_border()` is called once, creating a single `tcBorders` element with all four sides explicitly set (blue `single` left; `none` top, bottom, right). No XML ordering conflict. Result: only the blue left border accent is visible.

### d. Recommendations empty-title columns — PASS
`add_two_col_rec_table()` guards H3 rendering with `if title:`. Empty string is falsy, H3 is skipped. Verified at all 7 relevant call sites — bullet content renders correctly, empty cells render as blank. No missing heading rows, no blank heading rows rendered unintentionally.

### e. Cover letter verbatim copy — PASS
All three sections verified word-for-word against `jay_handoff/proposal.html`:
- "About this proposal" (2 paragraphs) — full match
- "Program highlights" (5 bullets) — full match
- "What is included in this proposal" (intro + 4 bullets with bold lead terms) — full match

Template tags: `{quoteDate}`, `{memberName}`, `{memberAddress}` throughout — correct camelCase single-brace format matching the docxtemplater data model. No `{{MEMBER_NAME}}` HTML-style variants anywhere in the script.

---

## Boilerplate Fidelity

### Pages 7–9 comparison result

**Page 7 — Coverage Recommendations (1 of 3): FULL MATCH**
Banner text, lead paragraph, section divider "Commercial Lines", all 8 subsection titles, and all bullet lists match verbatim against `proposal.html`.

**Page 8 — Coverage Recommendations (2 of 3): CONTENT MATCH**
All section dividers ("Commercial Lines (continued)", "Personal Lines", "Bond Recommendations"), all subsection titles, and all bullet lists match. The HTML uses `<h3>&nbsp;</h3>` placeholder headings in right columns; Python skips these (Nitpick N1 above) — content is correct.

**Page 9 — Employee Benefits Recommendations: FULL MATCH**
Banner, lead paragraph, all three section dividers, all subsection titles, all bullet lists, and the callout box text match verbatim.

---

## Consistency Audit

| Rule | Status |
|------|--------|
| All section title bars use `add_banner()` / `add_banner_continued()` | ✅ No inline manual banners |
| `set_row_header()` on all 4 page-break-spanning tables | ✅ 4 calls found, all correct |
| Vertical centering on all data table rows | ❌ Excluded Persons loop row missing (Issue I1) |
| `set_cell_margins()` on all data tables | ✅ All tables |
| All scalar tags: single-brace camelCase | ✅ No double-brace variants |
| All loop tags paired | ✅ 3 pairs, all matched |
| Only 1 `{%image}` tag (`stackedLogoBase64`) | ✅ |

---

## Spec Fidelity

All 20 acceptance criteria from the Build Report verified as met by CC analysis:
- ✅ All title bars full-width, consistent height via `add_banner()`
- ✅ Table header rows repeat (`set_row_header()` on 4 tables)
- ✅ Table cells vertically centered and reduced padding (with exception of I1)
- ✅ Cover letter section title bar removed
- ✅ Cover letter body text verbatim from Jay's HTML
- ✅ `add_banner_continued()` delegates correctly
- ✅ SIG Disclosure box — blue left border, light gray bg
- ✅ Pages 7–9 boilerplate exact match

---

## What to Fix (NEEDS-CHANGES)

**Tony — one fix required, ~2 lines:**

In `build_coverage_details_continued_page()`, after setting `ep_d0` and `ep_d1` margins, add vertical alignment:

```python
set_cell_margins(ep_d0, top=60, bottom=60, left=80, right=60)
set_cell_margins(ep_d1, top=60, bottom=60, left=80, right=60)
# ADD THESE TWO LINES:
ep_d0.vertical_alignment = WD_ALIGN_VERTICAL.CENTER
ep_d1.vertical_alignment = WD_ALIGN_VERTICAL.CENTER
```

Regenerate `master.docx` and re-sync to S3. No other changes required.

---

## Cycle 2 Re-check
- I1 fix: CONFIRMED — ep_d0/ep_d1 vertical_alignment = WD_ALIGN_VERTICAL.CENTER present in build_coverage_details_continued_page() (lines 1104–1105)
- Spot checks: Policy info rows (L529-530) ✅ | Coverage details rows (L807-808) ✅ | Coverage options rows (L925-926) ✅ | Employee Classification Schedule (L993) ✅ — no other rows missing vertical alignment
- Verdict: PASS
