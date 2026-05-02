# BUILD Assignment: ADO#2695
## Proposal Generator: NBAIS WC Template — Final Polish (Header Alignment, Cell Vertical Align, Column Widths)

---

## Context
- **Working directory:** `/home/fredw/projects/fip/` (monorepo root)
- **Service directory:** `services/proposal-generator/`
- **ADO WI:** #2695 (project: Legacy Work — historical; future FIP WIs go to Fortress)
- **Commit convention:** `fix(ADO#2695): <description>`
- **Prior commit:** `dd7052e` (ADO#2632 — deployed as proposal-generator-dev:27)
- **All changes in:** `services/proposal-generator/scripts/build-nbais-wc-template.py`

---

## Mandatory: Use Claude Code CLI
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cd /home/fredw/projects/fip
cat /tmp/ado2695-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Build Report **MUST** include CC invocation.

---

## IMPORTANT: Pre-Build Investigation Required

Before writing any code, you must investigate what's actually in the script vs. what the WI claims. ADO#2631 and ADO#2632 already made fixes for vertical alignment and column widths — the WI may be describing the pre-fix state. Do NOT blindly re-implement fixes that already exist.

**Investigation steps (do these FIRST via CC):**

1. **Read the current `build-nbais-wc-template.py`** — search for:
   - `WD_ALIGN_VERTICAL` / `vertical_alignment` — how many calls exist, which functions are covered?
   - `label_pct`, `label_w`, `CONTACT_BOX_W`, `SPACER_W`, `sig.*label` — what are the current column width values?
   - `build_cover_first_page_header` — what does the header function currently do? Does it add text?

2. **Determine true gap vs. already-fixed:**
   - If `vertical_alignment = WD_ALIGN_VERTICAL.CENTER` already appears extensively → the issue may be in docxtemplater loop-generated table rows (which the python-docx static build can't reach). The fix would be in the JS service's XML generation (`src/services/lobRenderer.js` or `documentRenderer.js`).
   - If column widths are already 30/70 → confirm the S3 master.docx reflects the latest build script (run `python3 scripts/build-nbais-wc-template.py` locally and check output).

---

## Issues to Fix

### Issue 1 — Cover Header: Top-Aligned, No Text (CONFIRMED REAL)
**Current state (from code inspection):** `build_cover_first_page_header()` adds a navy bar table to the first-page header with white bold text: "NBAIS Workers' Compensation Program". The header bar may also be vertically positioned in the middle of the header zone rather than flush to the top.

**Required fix:**
- **Remove all text** from the header bar — the cell paragraph should be empty (no runs). The navy bar is purely decorative.
- **Top-align** the bar within the header zone: the table row must be flush to the top of the header area. Achieve this by:
  - Setting header paragraph spacing to zero (`space_before=Pt(0)`, `space_after=Pt(0)`) on any paragraph before the table
  - Setting `section.header_distance = Inches(0)` or as small as possible (Word minimum ~0.1") so the bar starts at the physical page top
  - Setting the table's top margin/padding to 0
  - Using `exact` row height so the bar height is fixed and doesn't expand

**Code location:** `def build_cover_first_page_header(section):` — modify the run that adds text to instead add an empty run (or no run at all).

### Issue 2 — Vertical Alignment: Investigate Then Fix
**Pre-check:** Count `WD_ALIGN_VERTICAL.CENTER` in the script. If it already exists on all static cells, the remaining issue is likely in **docxtemplater loop-rendered rows** — table rows generated at render time via `{#loop}{/loop}` syntax that the python-docx build script never creates.

**If the issue is in docxtemplater loop rows:**
- Look in `src/services/lobRenderer.js` — functions that build XML for dynamic table rows (class schedule rows, exclusion rows, etc.)
- Look in `src/services/documentRenderer.js` — template rendering config
- In those XML-building functions, ensure each `<w:tc>` element includes `<w:tcPr><w:vAlign w:val="center"/></w:tcPr>`

**If the issue is in the build script (cells genuinely missing alignment):**
- Add `cell.vertical_alignment = WD_ALIGN_VERTICAL.CENTER` to any missed cells

### Issue 3 — Column Widths: Investigate Then Fix
**Pre-check:** Verify current values in the script:
- `add_kv_table` default `label_pct` — should be 30
- `CONTACT_BOX_W` / `SPACER_W` — should be ~4580 / ~200 twips
- Signature table label_w — should be ~20% of CONTENT_W

**If already correct in the script but wrong in rendered output:**
- The issue is that `master.docx` in S3 may be stale — run `python3 scripts/build-nbais-wc-template.py --sync` to regenerate and re-sync
- OR the column widths are set on static template cells but docxtemplater is overwriting them with its own table normalization

**If a table genuinely still uses equal widths:**
- Fix it to use explicit proportional widths per the WI spec

**Column width targets per WI:**
- Two-column label/value tables: 30% label / 70% value
- Signature table: 25% label / 75% line (note: WI says 25/75, prior fix was 20/80 — use 25/75 per this WI)
- Contact table: ~45% box / ~5-8% gutter / ~47-50% box (gutter very narrow)

---

## Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| 1 | Cover header bar is top-aligned, flush to page top | ✅/❌ |
| 2 | Cover header bar contains NO text — purely decorative navy bar | ✅/❌ |
| 3 | All table cells vertically centered (static + loop-rendered) | ✅/❌ |
| 4 | Two-column KV tables: 30% / 70% column split | ✅/❌ |
| 5 | Signature table: 25% label / 75% line | ✅/❌ |
| 6 | Contact table: narrow gutter (~5-8% of width), wide content boxes | ✅/❌ |
| 7 | Generation succeeds cleanly, S3 synced | ✅/❌ |

---

## Build & Test
```bash
cd /home/fredw/projects/fip

# Rebuild
python3 services/proposal-generator/scripts/build-nbais-wc-template.py

# Sync to S3
python3 services/proposal-generator/scripts/build-nbais-wc-template.py --sync

# Commit
git add -A && git commit -m "fix(ADO#2695): cover header text/alignment, vertical centering, proportional column widths"
```

---

## ADO Tracking
```bash
mcporter call devops.add_comment project="Legacy Work" id=2695 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {summary}. Build: SUCCEEDED. S3 synced."
```

## Deliverables
1. Build Report → `services/proposal-generator/pipeline/ADO2695-BUILD-REPORT.md`
2. ADO comment posted
3. Explicit note in Build Report: for each issue, state whether it was already partially/fully present in the code and what specifically was changed
