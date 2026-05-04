# BUILD Assignment: ADO#2728
## Proposal Generator: NBAIS WC Template v2 — Page 5 Row Height + Pages 7-9 List Layout Fixes

---

## Context
- **Working directory:** `/home/fredw/projects/fip/` (monorepo root)
- **Service directory:** `services/proposal-generator/`
- **ADO WI:** #2728 (project: Legacy Work)
- **Commit convention:** `fix(ADO#2728): <description>`
- **Prior state:** `proposal-generator-dev:32` (commit `acf9a25`)
- **Files to change:**
  - `services/proposal-generator/src/services/lobRenderer.js` — page 5 classification schedule rows
  - `services/proposal-generator/src/services/boilerplateRenderer.js` — pages 7-9 headings + spacing

---

## Mandatory: Use Claude Code CLI
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cd /home/fredw/projects/fip
cat /tmp/ado2728-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Build Report **MUST** include CC invocation.

---

## Issue 1 — Page 5: Classification Schedule Row Height (lobRenderer.js)

**Root cause:** The JS code that dynamically renders the Employee Classification Schedule data rows (in `lobRenderer.js`) is emitting trailing empty `<w:p/>` elements inside cells, causing row heights to expand beyond content. This is the JS-side equivalent of the python-docx issue fixed in ADO#2704.

**Fix in `lobRenderer.js`:**

Find the function that builds classification schedule table rows. For every cell in those rows, ensure:

1. **vAlign = "center"** on the cell properties — in docxtemplater/raw XML context, that means each `<w:tc>` should have `<w:vAlign w:val="center"/>` in its `<w:tcPr>`. In the JS XML building code, add this to the tcPr block.

2. **Paragraph spacing zero** on all paragraphs inside cells — each `<w:p>` in a cell should have `<w:spacing w:before="0" w:after="0"/>` in its `<w:pPr>`.

3. **No trailing empty paragraphs** — if the row-building code appends empty `<w:p/>` or `<w:p><w:pPr/></w:p>` elements after the content paragraph, remove them. Each cell should have exactly the content paragraphs needed, nothing more.

**How to locate:** Search `lobRenderer.js` for the classification schedule / class schedule table building code. Look for where it creates `<w:tr>` rows for each classification entry (those with `classCode`, `classDesc`, `payroll`, `estPremium` etc.). The fix applies to all cells in those data rows.

---

## Issue 2 — Pages 7-9: Stray [+] Icon + List Spacing (boilerplateRenderer.js)

### Part A — Strip outlineLvl from heading paragraphs

**Root cause:** Heading paragraphs on pages 7-9 have `<w:outlineLvl>` set in their paragraph properties (`<w:pPr>`). Word shows a [+] expand control (outline/navigation artifact) next to paragraphs with an outline level.

**Fix:** In `boilerplateRenderer.js`, wherever heading paragraphs are created (look for "Commercial Lines", "Life Department", "Retirement Plan Services" headings and any other section headings), ensure `<w:outlineLvl>` is NOT present in the `<w:pPr>`. 

If the headings are created by specifying a `style` that inherits an outline level, override it explicitly by setting `<w:outlineLvl w:val="9"/>` (value 9 = "Body Text" / no outline level) in the paragraph-level `<w:pPr>`, which overrides the style-level setting.

Pattern to add to each heading paragraph's pPr:
```xml
<w:outlineLvl w:val="9"/>
```

### Part B — Strip empty/spacer paragraphs

**Root cause:** HTML spacer `<div>` or `&nbsp;` elements from the original HTML source were converted to empty `<w:p/>` paragraphs in the docx output. These appear as extra blank lines before "Life Department" and "Retirement Plan Services" section items on page 9, and possibly elsewhere on pages 7-8.

**Fix:** In `boilerplateRenderer.js`, do a pass to remove empty paragraphs (paragraphs with no text content and no meaningful markup). The rule: if a paragraph has no runs (`<w:r>`) and no meaningful content, delete it. Spacing between sections should come from `<w:spacing w:before="..."/>` on the heading paragraph itself, not from blank paragraphs.

Alternatively, if the renderer is building paragraphs from an array of content items, filter out any items that would produce empty paragraphs.

---

## Implementation Approach

Use CC to:
1. Read both files to understand their current structure
2. Apply the fixes as described
3. Run the build: `python3 services/proposal-generator/scripts/build-nbais-wc-template.py`
4. Sync to S3 manually (known --sync subprocess bug workaround):
   ```bash
   aws s3 cp services/proposal-generator/templates/verticals/nbais-wc/master.docx \
     s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx \
     --profile fortress-tools-deployer --region us-east-1
   ```
5. Commit all JS changes

---

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| 1 | Classification schedule data cells have `vAlign=center` in tcPr |
| 2 | Classification schedule data cells have `spacing before=0 after=0` on paragraphs |
| 3 | No trailing empty paragraphs in classification schedule cells |
| 4 | Heading paragraphs on pages 7-9 have `outlineLvl w:val="9"` in pPr (suppress [+] icon) |
| 5 | Empty/spacer paragraphs removed from boilerplate sections |
| 6 | Build script runs clean, master.docx saved |
| 7 | S3 synced |

---

## Build & Test
```bash
cd /home/fredw/projects/fip
python3 services/proposal-generator/scripts/build-nbais-wc-template.py
# Then S3 sync manually
aws s3 cp services/proposal-generator/templates/verticals/nbais-wc/master.docx \
  s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx \
  --profile fortress-tools-deployer --region us-east-1
git add -A && git commit -m "fix(ADO#2728): classif schedule cell height (vAlign+spacing+no trailing paras), strip outlineLvl + empty paras from boilerplate pages 7-9"
```

---

## ADO Tracking
```bash
mcporter call devops.add_comment project="Legacy Work" id=2728 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: pg5 classif schedule row height fixed (vAlign+spacing+trailing para strip), pg7-9 outlineLvl stripped + empty spacer paras removed. Build: SUCCEEDED. S3 synced."
```

## Deliverables
1. Build Report → `services/proposal-generator/pipeline/ADO2728-BUILD-REPORT.md`
2. ADO comment posted
