# BUILD Assignment: ADO#2704
## Proposal Generator: NBAIS WC Template — Header Gap + Table Vertical Centering Root Cause Fix

---

## Context
- **Working directory:** `/home/fredw/projects/fip/` (monorepo root)
- **Service directory:** `services/proposal-generator/`
- **ADO WI:** #2704 (project: Legacy Work)
- **Commit convention:** `fix(ADO#2704): <description>`
- **Prior state:** `proposal-generator-dev:30` (commit `8db3a0a`)
- **All changes in:** `services/proposal-generator/scripts/build-nbais-wc-template.py`

---

## Mandatory: Use Claude Code CLI
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cd /home/fredw/projects/fip
cat /tmp/ado2704-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Build Report **MUST** include CC invocation.

---

## Issue 1 — Header Bar Gap at Top of Cover Page

**Root cause:** `section.header_distance = Inches(0.1)` sets the `w:header` attribute in `<w:pgMar>` to 144 twips. This creates a 0.1" gap between the top page edge and the header zone, so the blue bar doesn't reach the absolute top.

**Fix:** Set `w:header="0"` in the `<w:pgMar>` element directly via XML manipulation. The python-docx `header_distance` property maps to this attribute, so use:

```python
section.header_distance = Inches(0)
```

OR if that causes issues, manipulate the XML directly:
```python
from docx.oxml.ns import qn
pgMar = section._sectPr.find(qn('w:pgMar'))
if pgMar is not None:
    pgMar.set(qn('w:header'), '0')
```

**Location in script:** `build_cover_page()` or wherever `section.header_distance = Inches(0.1)` is set (around line 487 in current script). Also check `set_standard_section_props()`.

---

## Issue 2 — Table Vertical Centering Not Working

Two root causes — BOTH must be fixed together or the issue persists.

### Root Cause A — vAlign must be on each `<w:tc>`, not table-level

In OOXML, `<w:vAlign w:val="center"/>` inside `<w:tcPr>` **must be set on each individual `<w:tc>` element**. Setting it at table level (`<w:tblPr>`) does NOT cascade. The current code uses `cell.vertical_alignment = WD_ALIGN_VERTICAL.CENTER` which should be correct (sets on `<w:tc>`), but verify the XML is actually landing in `<w:tcPr>` not `<w:tblPr>`.

**Add a helper to set vAlign explicitly via XML** to guarantee correct placement:
```python
def set_cell_vAlign(cell, val='center'):
    """Set vAlign on cell's tcPr, removing any existing vAlign first."""
    tc = cell._tc
    tcPr = tc.get_or_add_tcPr()
    # Remove existing vAlign
    for existing in tcPr.findall(qn('w:vAlign')):
        tcPr.remove(existing)
    vAlign = OxmlElement('w:vAlign')
    vAlign.set(qn('w:val'), val)
    tcPr.append(vAlign)
```

Replace all `cell.vertical_alignment = WD_ALIGN_VERTICAL.CENTER` calls with `set_cell_vAlign(cell, 'center')`.

### Root Cause B — Paragraph spacing inside cells

Every paragraph inside every table cell must have `<w:spacing w:before="0" w:after="0"/>` explicitly set. Inherited Normal style spacing (typically `w:after="160"` or `w:after="200"`) creates visual padding that makes content appear top-aligned even when vAlign is correct.

Also remove trailing empty `<w:p/>` elements at the end of cell content.

**Add a helper:**
```python
def set_para_spacing_zero(para):
    """Set paragraph spacing before=0 after=0."""
    pPr = para._p.get_or_add_pPr()
    for existing in pPr.findall(qn('w:spacing')):
        pPr.remove(existing)
    spacing = OxmlElement('w:spacing')
    spacing.set(qn('w:before'), '0')
    spacing.set(qn('w:after'), '0')
    pPr.append(spacing)

def fix_cell_content(cell):
    """Apply vAlign=center, zero paragraph spacing, remove trailing empty paras."""
    set_cell_vAlign(cell, 'center')
    paras = cell.paragraphs
    # Remove trailing empty paragraphs (keep at least one)
    while len(paras) > 1 and not paras[-1].text.strip():
        p = paras[-1]._p
        p.getparent().remove(p)
        paras = cell.paragraphs
    # Zero spacing on all remaining paragraphs
    for para in cell.paragraphs:
        set_para_spacing_zero(para)
```

**Apply `fix_cell_content(cell)` to every data cell in every table** across the entire build script. This replaces the pattern of individually setting `cell.vertical_alignment`.

### Scope — all tables
Apply to every table in the script:
- Cover meta table (Prepared For / Policy Period / etc.)
- Coverage at a Glance table
- Premium Summary tables
- Coverage Details / Limits table
- Class Schedule table
- Excluded Persons table
- WC Exclusions table
- SIG Disclosure box (single-cell table)
- Contact boxes (page 6)
- Signature table
- All boilerplate recommendation tables (pages 7–9)

---

## Implementation Approach

The cleanest approach is:
1. Add `set_cell_vAlign()`, `set_para_spacing_zero()`, and `fix_cell_content()` helpers near the other cell helpers
2. Do a global search-and-replace: wherever `cell.vertical_alignment = WD_ALIGN_VERTICAL.CENTER` appears, change to `fix_cell_content(cell)` (which handles vAlign + spacing + trailing paras)
3. For any cells where `vertical_alignment` was set but spacing/trailing-para fix wasn't, add the full `fix_cell_content()` call

---

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| 1 | `w:header="0"` in pgMar — header bar flush to page top |
| 2 | Every `<w:tc>` in every table has `<w:vAlign w:val="center"/>` in its `<w:tcPr>` |
| 3 | Every paragraph in every table cell has `<w:spacing w:before="0" w:after="0"/>` |
| 4 | No trailing empty `<w:p/>` elements at end of cell content |
| 5 | Build script runs clean, `master.docx` saved |
| 6 | S3 synced |

---

## Build & Test
```bash
cd /home/fredw/projects/fip
python3 services/proposal-generator/scripts/build-nbais-wc-template.py
python3 services/proposal-generator/scripts/build-nbais-wc-template.py --sync
git add -A && git commit -m "fix(ADO#2704): header pgMar w:header=0, cell vAlign+spacing zero fix"
```

---

## ADO Tracking
```bash
mcporter call devops.add_comment project="Legacy Work" id=2704 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {summary}. Build: SUCCEEDED. S3 synced."
```

## Deliverables
1. Build Report → `services/proposal-generator/pipeline/ADO2704-BUILD-REPORT.md`
2. ADO comment posted
3. Build report must note: count of cells updated, whether `fix_cell_content()` helper was added, and any tables where the fix was NOT applied (with reason)
