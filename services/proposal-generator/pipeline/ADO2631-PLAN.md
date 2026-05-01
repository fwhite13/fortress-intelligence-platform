# BUILD Assignment: ADO#2631
## Proposal Generator: NBAIS WC Template Fidelity Pass — Full Visual Match to Jay Reference

---

## Context
- **Working directory:** `/home/fredw/projects/fip/` (monorepo root — always cd here first)
- **Service directory:** `services/proposal-generator/`
- **ADO WI:** #2631 (Legacy Work project)
- **Commit convention:** `fix(ADO#2631): <description>`
- **Review cycle:** 1 of 2

---

## Mandatory: Use Claude Code CLI
Write a detailed brief file, then execute:
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cat brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Your Build Report **MUST** include the CC invocation used. Do NOT use edit/write tools directly for code changes.

---

## Reference Assets (READ THESE FIRST)
Before writing any code, read the reference materials in this order:

1. `services/proposal-generator/jay_handoff/proposal.html` — source of truth for all copy text
2. `services/proposal-generator/jay_handoff/styles.css` — source of truth for visual styling
3. `services/proposal-generator/jay_handoff/NBAIS WC Proposal.pdf` — page-by-page visual reference (use `pdf` tool or image analysis)
4. `services/proposal-generator/templates/verticals/nbais-wc/master.docx` — current template (read via python-docx or inspection)
5. `services/proposal-generator/src/services/` — JS service files for any dynamic generation logic

---

## Acceptance Criteria

### Global Rules (ALL pages/tables)
- [ ] All blue section/page title bars (e.g. "Premium Summary", "Coverage Details", "Next Steps & Member Authorization") must be full-width and the same height as the table header rows — standardize all bars to the same full-width, consistent-height style
- [ ] All table cells: text vertically centered; reduced cell padding; label columns narrower than value columns to minimize text wrapping
- [ ] Table headers repeat when a table spans a page break

### Page 1 — Cover
- [ ] Stacked logo renders with correct aspect ratio (no distortion) — use `logo_stacked.png` dimensions
- [ ] Blue bar at top is a real Word **header section** (not body content)
- [ ] Blue horizontal rule + confidentiality text at bottom is a real Word **footer section** (not body content)
- [ ] Title has line break: "Workers' Compensation" on first line, "Insurance Proposal" on second line
- [ ] "Prepared For" through "Program" info table is horizontally centered on the page

### Page 2 — Cover Letter
- [ ] "Cover Letter" section title bar removed entirely
- [ ] Body text replaced verbatim with Jay's sample copy from `proposal.html` — only dynamic fields are `{quoteDate}`, `{insured.name}`, and member address fields

### Page 3 — Premium Summary
- [ ] Global bar height/width fix applied
- [ ] Global table formatting applied (vertical centering, padding, column widths)
- [ ] "What's next" section appears on this page, after the Coverage at a Glance table. Text: "Review the Coverage Details on the following page, confirm payroll and class code accuracy, and contact your NBAIS producer to bind. Final premium will be reconciled at audit."
- [ ] Explicit page break before "Coverage Details — Workers' Compensation" section (forces it to page 4)

### Page 4 — Coverage Details
- [ ] Global bar and table formatting applied
- [ ] Surplus Contribution explanatory text matches verbatim:
  "As a self-insured group (SIG), BAWNSIG requires a surplus contribution in addition to the estimated premium. This contribution — calculated at 8% of the estimated premium — is a regulatory requirement for SIG participation in Nevada and supports the financial reserves of the group. **It is not a fee retained by NBAIS or your producer.**" (last sentence bold)

### Page 5 — WC Exclusions / SIG Disclosure
- [ ] Global bar and table formatting applied
- [ ] "Self-Insured Group Disclosure" box has left blue border accent + light gray background (matching sample)
- [ ] SIG Disclosure text matches verbatim:
  "BAWNSIG is a Nevada-regulated self-insured group, not a traditional insurance carrier, and therefore does not carry an AM Best financial strength rating. BAWNSIG operates under the regulatory oversight of the Nevada Division of Industrial Relations and maintains reserves in accordance with state requirements. Members of NBAIS benefit from the group's long-standing solvency and claims-paying history as a construction industry SIG in Nevada."

### Page 6 — Next Steps & Member Authorization
- [ ] Global bar fix applied to "Next Steps & Member Authorization" header bar
- [ ] Intro paragraph text matches verbatim:
  "To bind coverage or to discuss this proposal in further detail, please contact your NBAIS producer using the information below. Please review all coverage details carefully and confirm payroll and class code accuracy prior to binding, as final premium is subject to audit."
- [ ] Producer/Office contact boxes render as two side-by-side shaded boxes with NO outer border (not a plain 2-column bordered table)
- [ ] Member Authorization text matches verbatim:
  "By signing below, the undersigned acknowledges receipt of this Workers' Compensation Insurance proposal and authorizes Nevada Builders Alliance Insurance Services (NBAIS) to bind coverage as described herein, effective on the policy period stated above. The undersigned confirms that the payroll, classification codes, and excluded persons listed in this proposal are accurate to the best of their knowledge and understands that final premium is subject to audit. The required initial down payment will be remitted online via the secure payment link provided upon binding."
- [ ] Signature lines: label (By, Print Name, Title, Date) on left, signature line extends right on SAME row — NOT on separate rows. Match sample layout exactly.
- [ ] Erroneous red "What's Next" heading before Member Authorization is removed
- [ ] Proposal disclaimer text matches verbatim:
  "This proposal is not a binder or guarantee of coverage. All coverage is subject to underwriting approval, policy terms, conditions, and exclusions. Premium estimates are subject to final payroll audit. NBAIS is an insurance program administered on behalf of Nevada Builders Alliance members."

### Pages 7–9 — Boilerplate Recommendations
- [ ] Text content and list styling exactly matches `jay_handoff/proposal.html` — direct port from HTML source, do not reinterpret. Current output has text/list formatting differences vs. HTML source.

---

## Implementation Notes

### Template Authoring (master.docx)
- The template is a `python-docx`/`docxtemplater` template. `master.docx` is generated via `scripts/build-nbais-wc-template.py` — **DO NOT hand-edit master.docx directly**. All changes go into the build script.
- Image tags use `{%tagName}` prefix (docxtemplater-image-module-free syntax)
- Raw XML injection uses `{@tagName}` prefix (RawXmlModule syntax)
- Scalar tags use `{tagName}` syntax

### Word Header/Footer for Cover Page
- To make the blue bar a real Word header and the confidentiality footer a real Word footer, the build script must use `python-docx` `sections[0].header` and `sections[0].footer` APIs
- The cover page likely needs a "different first page" header (`sections[0].different_first_page_header_footer = True`) so only the cover gets its special header/footer
- Interior pages need their own header (with the horizontal logo) and footer

### Table Formatting
- All table cells: `tcPr/vAlign` = `center`
- Cell margins: reduce to ~50 twips (from default ~108)
- Label/value ratio: for 2-column label+value tables, target ~35%/65% column widths
- Table header rows: `tblHeader = True` on first row's `trPr` to enable repeat on page break

### Full-width Title Bars
- Title bars are single-cell tables spanning the full text width (matches table header row widths exactly)
- Consistent height: set row height explicitly (e.g., 400 twips / ~0.28 inches)

### Producer Contact Boxes (Page 6)
- Two-column layout with shaded cells, no outer border
- Inner cell shading: light gray (e.g., `E8E8E8`)
- All outer borders: none/zero width; only inner vertical divider visible

### Signature Lines (Page 6)
- Each signature line is a 2-column table row: narrow label cell (left) + wide underline cell (right)
- The underline cell uses bottom border only to simulate the signature line

### Build & Test Commands
```bash
cd /home/fredw/projects/fip

# Build the template (regenerates master.docx from the script)
python3 services/proposal-generator/scripts/build-nbais-wc-template.py

# If build script doesn't exist as a regenerator, check if master.docx must be manipulated directly
# (inspect the script first to understand the pattern)

# Run full generation test
cd services/proposal-generator
node -e "
const fs = require('fs');
const payload = JSON.parse(fs.readFileSync('test-payloads/nbais-wc-test.json', 'utf8'));
const axios = require('axios');
// Or use the local generation function directly
"

# Or run the service locally and POST to it:
# npm start & sleep 3 && curl -s -X POST http://localhost:3000/proposals/generate \
#   -H 'Content-Type: application/json' \
#   -d @test-payloads/nbais-wc-test.json -o /tmp/test-output.docx

# Sync to S3 after successful test
python3 services/proposal-generator/scripts/build-nbais-wc-template.py --sync
```

---

## ADO Tracking (MANDATORY)
After your build is complete, post a comment to ADO#2631:

```bash
mcporter call devops.add_comment project="Legacy Work" id=2631 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {one-line summary}. Build: SUCCEEDED. Files changed: {list}."
```

---

## Deliverables
1. All changes committed with `fix(ADO#2631): <description>`
2. `python3 scripts/build-nbais-wc-template.py --sync` executed successfully (S3 synced)
3. Test generation output verified against all 9 pages
4. Build Report saved to `services/proposal-generator/pipeline/ADO2631-BUILD-REPORT.md` (include CC invocation used)

### Build Report Format
```markdown
# Build Report: ADO#2631
## Status: SUCCEEDED / FAILED
## CC Invocation
`cat brief.md | claude --model sonnet --print --dangerously-skip-permissions`
## Commits
- {hash}: {description}
## Files Changed
- {file list}
## Acceptance Criteria
| # | Criterion | Status |
|---|-----------|--------|
| 1 | Global: title bars full-width | ✅/❌ |
| ... | | |
## Test Results
- Generation: PASS/FAIL
- All 9 pages verified: YES/NO
- S3 sync: PASS/FAIL
## Notes
{anything Clint should know for review}
```
