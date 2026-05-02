# BUILD Assignment: ADO#2632
## Proposal Generator: NBAIS WC Template — Remaining Fidelity Issues After ADO#2631

---

## Context
- **Working directory:** `/home/fredw/projects/fip/` (monorepo root — always cd here first)
- **Service directory:** `services/proposal-generator/`
- **ADO WI:** #2632 (Legacy Work project)
- **Commit convention:** `fix(ADO#2632): <description>`
- **Prior commit:** `de138c5` (ADO#2631 fidelity pass — deployed as proposal-generator-dev:26)
- **Review cycle:** 1 of 2
- **All changes go in:** `services/proposal-generator/scripts/build-nbais-wc-template.py`

---

## Mandatory: Use Claude Code CLI
Write a detailed brief file at `/tmp/ado2632-brief.md`, then execute:
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cd /home/fredw/projects/fip
cat /tmp/ado2632-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Your Build Report **MUST** include the CC invocation used.

---

## Reference Assets (READ THESE FIRST)
1. `services/proposal-generator/jay_handoff/proposal.html` — source of truth for copy/structure
2. `services/proposal-generator/jay_handoff/styles.css` — source of truth for visual styling
3. `services/proposal-generator/jay_handoff/NBAIS WC Proposal.pdf` — page-by-page visual reference
4. `services/proposal-generator/scripts/build-nbais-wc-template.py` — current template build script
5. `services/proposal-generator/templates/verticals/nbais-wc/logo_stacked.png` — get its actual pixel dimensions

---

## Issues to Fix

### Issue 1 — Cover Page: Real Word Header Section (BLOCKING)
**Problem:** The blue bar at the top of the cover page is still body content (a table in the document body). It must be a real Word **header section**.

**Implementation:**
- Use python-docx `section.header` API to populate the first-page header
- The cover page section must have `different_first_page_header_footer = True`
- The blue bar (navy background, white bold text "NBAIS Workers' Compensation Program") goes in `section.first_page_header`
- Interior pages use a separate header (horizontal logo — already implemented or needs verification)
- The body-content blue bar at the top of the cover must be **removed** from body content

**python-docx API pattern:**
```python
from docx.oxml.ns import qn
from docx.oxml import OxmlElement

section = doc.sections[0]
section.different_first_page_header_footer = True

# First page header
first_header = section.first_page_header
first_header.is_linked_to_previous = False
# Clear existing content
for para in first_header.paragraphs:
    p = para._p
    p.getparent().remove(p)
# Add blue bar table to header
# (use same add_banner() helper but targeted at header instead of doc body)
```

### Issue 2 — Cover Page: Real Word Footer Section (BLOCKING)
**Problem:** The blue horizontal rule + confidentiality text at the bottom of the cover is still body content. Must be a real Word **footer section**.

**Implementation:**
- Use `section.first_page_footer` for the cover page footer
- Content: blue horizontal rule (top border on a paragraph, or a thin navy table row) + "CONFIDENTIAL — This proposal is intended solely for the use of the named insured and their authorized representatives." in small italic text
- Remove the body-content footer block from the cover page body

### Issue 3 — Logo Aspect Ratio (BLOCKING)
**Problem:** The stacked logo is visibly squished/distorted — it's being stretched to fill a width without preserving the natural aspect ratio.

**Implementation:**
- Read the actual pixel dimensions of `logo_stacked.png`: `python3 -c "from PIL import Image; img = Image.open('services/proposal-generator/templates/verticals/nbais-wc/logo_stacked.png'); print(img.size)"`
- Calculate the natural aspect ratio (height/width)
- In the build script, set the logo image width to the desired display width (e.g., 2 inches) and compute height = width × (natural_height / natural_width)
- Do NOT hardcode a height that doesn't match the aspect ratio
- Use `docxtemplater-image-module-free` sizing: the image module config accepts `getSize` function or fixed width/height — ensure height is computed from actual aspect ratio

### Issue 4 — "Prepared For" Table Centering (BLOCKING)
**Problem:** The Prepared For/Policy Period/Prepared By/Date/Program meta table on the cover page is right-skewed.

**Implementation:**
- The table must be set to `WD_TABLE_ALIGNMENT.CENTER`
- Also verify the table width — if it's set to full text width, centering has no visible effect. Set it to a narrower width (e.g., 60–70% of text width) so it visually centers on the page.
- Check if the issue is actually the table width being set to `CONTENT_W` — if so, reduce it to ~4500 twips (approx 3.125") and center it.

### Issue 5 — Global: All Table Cells Vertically Centered (BLOCKING)
**Problem:** Text is still vertically top-aligned in all table cells. The `WD_ALIGN_VERTICAL.CENTER` fix from ADO#2631 was only applied to some tables, not all.

**Implementation:**
- Audit every table-building function in `build-nbais-wc-template.py`
- For EVERY cell in EVERY data table, ensure `cell.vertical_alignment = WD_ALIGN_VERTICAL.CENTER` is set
- Write a helper or apply systematically — this must be truly global, no exceptions
- Tables to verify: cover meta table, all premium summary tables, all coverage detail tables, exclusions table, excluded persons table, contact boxes, signature table, every recommendations table

### Issue 6 — Global: Intelligent Column Widths (BLOCKING)
**Problem:** Column widths are equal across all two-column tables. Should be ~30% label / ~70% value.

**Implementation:**
- For all two-column label/value tables: set label column to ~30% of table width, value column to ~70%
- Apply this to: Prepared For meta table, Coverage at a Glance, Coverage and Limits details, Employee Classification Schedule (adjust to content), Exclusions, Excluded Persons
- Multi-column tables: size to content (e.g., class code tables — code narrow, description wide, rate/payroll/premium medium)
- Do NOT apply 30/70 to the contact boxes (those are 50/50 side-by-side layout)
- Signature table: label column narrow (~20%), signature line column wide (~80%)

### Issue 7 — Page 6: Gap Between Contact Boxes
**Problem:** The two grey contact boxes (Your NBAIS Producer / NBAIS Program Office) are touching with no gap.

**Implementation:**
- The contact boxes are a 2-column table. Add a narrow center spacer column between them (e.g., 200 twips / ~0.14") with no borders and no fill
- OR use cell spacing (`tblCellSpacing`) on the table
- The two content cells keep their shading and content; the center spacer cell is empty and transparent
- Result: visual gap between the two boxes

**python-docx table spacing:**
```python
# Option A: Add spacer column
# col 0: Producer box (~3100 twips)
# col 1: spacer (~200 twips, no shading, no borders)  
# col 2: Office box (~3100 twips)

# Option B: tblCellSpacing XML
tbl = contact_table._tbl
tblPr = tbl.find(qn('w:tblPr'))
spacing = OxmlElement('w:tblCellSpacing')
spacing.set(qn('w:w'), '120')
spacing.set(qn('w:type'), 'dxa')
tblPr.append(spacing)
```

### Issue 8 — Page 6: Signature Lines Full Width
**Problem:** Signature lines (By, Print Name, Title, Date) span only partial width and are right-aligned.

**Implementation:**
- Each signature row is a 2-column table: narrow label cell (~20% / ~1200 twips) + wide signature line cell (~80% / ~4900 twips)
- The signature line cell must use bottom border only (the underline effect) spanning the full remaining width to the right margin
- Ensure `set_cell_width()` is called on both cells and sums to full `CONTENT_W`
- Bottom border on signature cell: `w:color="000000"`, `w:sz="6"`, `w:val="single"`

### Issue 9 — Pages 7–9: "Discuss with your producer" Callout Box Left Border
**Problem:** The callout box is a plain shaded box with no left border accent and too-tight padding.

**Implementation:**
- Match the SIG Disclosure box pattern from page 5 (ADO#2631 — already implemented there)
- Dark navy/dark blue left border: `w:val="single"`, `w:sz="36"` (4.5pt), `w:color="1F3864"` (or `"2E4053"` — dark navy)
- Light blue/gray background fill: `"E8F0FE"` or `"EBF3FF"` (light blue-gray, slightly different from SIG Disclosure's `"E8E8E8"`)
- Generous internal padding: `top=120, bottom=120, left=160, right=120` twips
- Apply to every "Discuss with your producer" callout box across pages 7–9 (there may be multiple instances — one per recommendations page)

---

## Acceptance Criteria

| # | Issue | Criterion |
|---|-------|-----------|
| 1 | Cover header | Blue bar in real Word first-page header section; removed from body |
| 2 | Cover footer | Confidentiality text in real Word first-page footer section; removed from body |
| 3 | Logo aspect ratio | Stacked logo renders at natural aspect ratio (no squish/distortion) |
| 4 | Prepared For centering | Meta table centered horizontally on cover page (narrower width + center alignment) |
| 5 | Global vertical alignment | ALL table cells have `WD_ALIGN_VERTICAL.CENTER` — verified exhaustively |
| 6 | Global column widths | All two-column label/value tables use ~30/70 split; multi-column sized to content |
| 7 | Contact box gap | Visual whitespace gap between Producer and Office boxes on page 6 |
| 8 | Signature lines full width | Each signature line cell extends to right margin (full text width) |
| 9 | Callout box border | "Discuss with your producer" boxes have thick dark navy left border + light bg + generous padding |

---

## Implementation Notes

### Word Header/Footer — Key Gotchas
1. **`is_linked_to_previous`** must be `False` on the first-page header/footer, otherwise Word ignores the content
2. **`different_first_page_header_footer = True`** must be set on the section BEFORE accessing `first_page_header`
3. The header/footer content area in python-docx is accessed via `.paragraphs` — to add a table to a header, use `header.add_table()` or manipulate XML directly
4. After setting the header, the corresponding body-content element MUST be removed to avoid duplication

### Logo Sizing
The logo is rendered via docxtemplater-image-module-free. Check how the image size is currently configured in the build script — look for `getSize` or `size` in the image module config, or look for where `stackedLogoBase64` is defined and how width/height are set on the image placeholder in `master.docx`. Fix the height to match natural aspect ratio.

### Column Width Math
- `CONTENT_W` is likely defined near the top of the script (e.g., 6120 twips for a standard 8.5" page with 1.25" margins)
- 30% of 6120 = 1836 twips (label)
- 70% of 6120 = 4284 twips (value)
- Always verify: label_w + value_w == CONTENT_W (no gaps, no overflow)

---

## Build & Test
```bash
cd /home/fredw/projects/fip

# Rebuild template
python3 services/proposal-generator/scripts/build-nbais-wc-template.py

# Verify no Python errors, master.docx saved

# Sync to S3
python3 services/proposal-generator/scripts/build-nbais-wc-template.py --sync

# Commit
git add -A && git commit -m "fix(ADO#2632): cover header/footer sections, logo aspect ratio, column widths, contact box gap, signature lines, callout box border"
```

---

## ADO Tracking (MANDATORY)
```bash
mcporter call devops.add_comment project="Legacy Work" id=2632 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {summary}. Build: SUCCEEDED. S3 synced."
```

---

## Deliverables
1. All 9 issues fixed and committed
2. `python3 scripts/build-nbais-wc-template.py --sync` executed successfully
3. Build Report saved to `services/proposal-generator/pipeline/ADO2632-BUILD-REPORT.md`

### Build Report Format
```markdown
# Build Report: ADO#2632
## Status: SUCCEEDED / FAILED
## CC Invocation
[command used]
## Commits
- {hash}: {description}
## Files Changed
- {list}
## Acceptance Criteria
| # | Criterion | Status | Notes |
|---|-----------|--------|-------|
| 1 | Cover header real Word section | ✅/❌ | |
| 2 | Cover footer real Word section | ✅/❌ | |
| 3 | Logo aspect ratio | ✅/❌ | natural dimensions used |
| 4 | Prepared For centering | ✅/❌ | |
| 5 | Global vertical alignment | ✅/❌ | N cells updated |
| 6 | Global column widths | ✅/❌ | N tables updated |
| 7 | Contact box gap | ✅/❌ | |
| 8 | Signature lines full width | ✅/❌ | |
| 9 | Callout box left border | ✅/❌ | |
## S3 Sync
PASS / FAIL
## Notes for Clint
[anything to flag for review]
```
