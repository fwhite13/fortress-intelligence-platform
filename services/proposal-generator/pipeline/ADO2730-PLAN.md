# BUILD Assignment: ADO#2730
## Proposal Generator: NBAIS WC Template v2 — Page 5 Data Trimming + Pages 7-9 Column Alignment + Empty Line Fixes

---

## Context
- **Working directory:** `/home/fredw/projects/fip/` (monorepo root)
- **Service directory:** `services/proposal-generator/`
- **ADO WI:** #2730 (project: Legacy Work)
- **Commit convention:** `fix(ADO#2730): <description>`
- **Prior state:** `proposal-generator-dev:33` (commit `fc62a2e`)
- **Files to change:**
  - `services/proposal-generator/src/services/assembleTemplateData.js` OR `lobRenderer.js` — Fix 1 (JS .trim())
  - `services/proposal-generator/scripts/build-nbais-wc-template.py` — Fix 2 (vAlign top) + Fix 3 (empty para + outlineLvl)

---

## Mandatory: Use Claude Code CLI
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cd /home/fredw/projects/fip
cat /tmp/ado2730-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Build Report **MUST** include CC invocation.

---

## Fix 1 — Page 5: Trim All Dynamic Cell String Values (JS)

**Root cause:** String values from the payload (state, estPremium, name, electionForm) have leading spaces or trailing CR/LF characters. These are rendering as visible whitespace in the generated cells, inflating cell content height or showing stray whitespace.

**Where to fix:** Find in `assembleTemplateData.js` (nbais-wc branch) or `lobRenderer.js` — wherever classification schedule rows and excluded persons rows are assembled.

**Fix:** Apply `.trim()` universally to ALL string values before they are inserted into table cell content. Do not cherry-pick just the known bad fields — trim everything. Pattern:

```javascript
// Instead of: value
// Use: typeof value === 'string' ? value.trim() : value

// Or a helper:
const trimVal = v => (typeof v === 'string' ? v.trim() : v);
```

Apply to:
- Classification schedule: `state`, `estPremium`, all other string fields in those rows
- Excluded persons: `name`, `electionForm`, all other string fields
- Any other dynamic table cell content in the nbais-wc data assembly

---

## Fix 2 — Pages 7-9: Two-Column Boilerplate Tables Must Be vAlign=top

**Root cause:** The global `fix_cell_content()` helper sets `vAlign=center` on all cells. The boilerplate two-column layout tables on pages 7-9 need `vAlign=top` — category headings must start at the top of their cell, not centered. When left and right columns have different heights, `vAlign=center` mis-aligns the shorter column's heading.

**This is the ONE exception to the global center rule — boilerplate two-column tables only.**

**Fix in `build-nbais-wc-template.py`:**

In the boilerplate section builder (look for the two-column table construction on pages 7-9 — the function(s) that build rows with `add_h3` + bullet items on left and right), after calling `fix_cell_content()` on each cell, override the vAlign back to top:

```python
def fix_cell_content_top(cell):
    """Like fix_cell_content but with vAlign=top — for boilerplate two-column tables."""
    fix_cell_content(cell)  # applies spacing zero + trailing para removal
    set_cell_vAlign(cell, 'top')  # override center → top
```

OR: pass a `valign` parameter if `fix_cell_content` already supports it (check — Tony added `valign` param in ADO#2704/2704 cycle 2):

```python
fix_cell_content(cell, valign='top')  # if supported
```

Apply `vAlign=top` to ALL cells in ALL two-column layout rows in the boilerplate section (pages 7-9). The right-column cells with no heading (e.g. "Farm & Ranch, Watercraft, Personal Articles Floater" in Personal Insurance) also get `vAlign=top` — this automatically aligns them with the left column's first item.

---

## Fix 3 — Pages 7-9: Remove Empty Paragraph After Top-Level Headings + [+] Icon

### Part A — Remove empty paragraph between section heading and first sub-heading

There is an empty `<w:p/>` between top-level section headings ("Commercial Lines", "Personal Lines", "Bond Recommendations") and their first sub-heading ("Property Coverages", etc.). Remove it.

In `build-nbais-wc-template.py`, find where these top-level headings are added (look for `add_section_divider` or the main section heading paragraph builder for pages 7-9). The empty paragraph that follows the heading — and precedes the first `add_h3` call — must be removed.

**Check the structure:** if the code does:
```python
add_section_divider(doc, "Commercial Lines")
doc.add_paragraph("")  # ← DELETE THIS
add_h3(doc, "Property Coverages")
```
Then delete the `doc.add_paragraph("")` call between them.

### Part B — [+] outline icon still showing

The `outlineLvl=9` fix from ADO#2728 was applied to `add_h3` and `add_section_divider` helpers' individual paragraph pPr. However the [+] icon may come from the **paragraph style itself** (e.g., if the paragraph uses a "Heading 2" or similar Word style that has `outlineLvl` baked into the style definition). Paragraph-level pPr overrides style-level, but only if Word respects the override — which it doesn't always for outline level from named styles.

**Fix:** If the top-level heading paragraphs use a named style (e.g., `para.style = doc.styles['Heading 2']`), either:
1. Change them to use the Normal or a custom style without outline level, OR
2. Ensure the `set_outline_level(para, 9)` call is made AFTER the style is set (it should override the style's outline level at the paragraph level)

Check whether the `set_outline_level()` call added in ADO#2728 is being called AFTER any `para.style = ...` assignments — if the style is set after `set_outline_level()`, the style's outline level would re-appear. Fix the ordering if needed.

---

## Implementation Approach

Use CC to:
1. Read both files to understand current structure
2. Apply all 3 fixes
3. Rebuild: `python3 services/proposal-generator/scripts/build-nbais-wc-template.py`
4. S3 sync manually: `aws s3 cp services/proposal-generator/templates/verticals/nbais-wc/master.docx s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx --profile fortress-tools-deployer --region us-east-1`
5. Commit all changes

---

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| 1 | `state` field in classification schedule has no leading/trailing whitespace |
| 2 | `estPremium` has no trailing CR/LF |
| 3 | `name` in excluded persons has no leading/trailing whitespace |
| 4 | `electionForm` has no trailing CR/LF |
| 5 | All dynamic table cell strings trimmed universally |
| 6 | Two-column boilerplate table cells on pages 7-9: vAlign=top |
| 7 | "Farm & Ranch" etc. right-column items start at top of cell |
| 8 | Empty paragraph between section headings and first sub-heading removed |
| 9 | [+] outline icon not present (outlineLvl=9 confirmed in paragraph pPr, after style set) |
| 10 | Build runs clean, S3 synced |

---

## Build & Test
```bash
cd /home/fredw/projects/fip
python3 services/proposal-generator/scripts/build-nbais-wc-template.py
aws s3 cp services/proposal-generator/templates/verticals/nbais-wc/master.docx \
  s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx \
  --profile fortress-tools-deployer --region us-east-1
git add -A && git commit -m "fix(ADO#2730): trim cell strings universally, boilerplate tables vAlign=top, remove empty paras, fix outlineLvl ordering"
```

---

## ADO Tracking
```bash
mcporter call devops.add_comment project="Legacy Work" id=2730 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: Fix1 .trim() on all dynamic cell values, Fix2 vAlign=top on pages 7-9 two-col tables, Fix3 empty para removed + outlineLvl=9 ordering fixed. Build: SUCCEEDED. S3 synced."
```

## Deliverables
1. Build Report → `services/proposal-generator/pipeline/ADO2730-BUILD-REPORT.md`
2. ADO comment posted
