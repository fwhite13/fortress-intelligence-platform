# REVIEW Assignment: ADO#2631
## Proposal Generator: NBAIS WC Template Fidelity Pass

---

## Context
- **ADO WI:** #2631 (Legacy Work project)
- **Review Cycle:** 1 of 2
- **Commit:** `35e25ca` — fix(ADO#2631): NBAIS WC template fidelity pass — full visual match to Jay reference
- **Working directory:** `/home/fredw/projects/fip/` (monorepo root)

---

## Mandatory: Use Claude Code CLI
Write a review brief, then execute:
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cat review-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Your Review Report **MUST** include the CC invocation used.

---

## Task Brief
NBAIS WC Word template fidelity pass. All changes are in `services/proposal-generator/scripts/build-nbais-wc-template.py` (the template build script) and the regenerated `templates/verticals/nbais-wc/master.docx`.

Key reference: `services/proposal-generator/jay_handoff/proposal.html` — source of truth for all copy text.

## Build Report
See: `services/proposal-generator/pipeline/ADO2631-BUILD-REPORT.md`

## Files Modified
- `services/proposal-generator/scripts/build-nbais-wc-template.py` — all template logic
- `services/proposal-generator/templates/verticals/nbais-wc/master.docx` — regenerated output

## What to Review

### 1. Code Review — `build-nbais-wc-template.py`
Use CC to read the full build script and review for:
- Correctness of python-docx API usage (table borders, cell shading, run formatting)
- Tag syntax: `{%tagName}` for images, `{@tagName}` for raw XML, `{tagName}` for scalars — no cross-contamination
- `set_row_header()` applied to all 4 tables that can span page breaks
- `add_banner()` used consistently (no leftover `add_banner_continued()` divergence)
- Cover letter template tags match actual data model: `{memberName}`, `{memberAddress}`, `{quoteDate}` — NOT `{{MEMBER_NAME}}` or other variants
- No leftover old banners, headings, or copy blocks that should have been removed

### 2. Specific Edge Cases (from Build Report)
Verify Tony's self-identified risks:

**a. `add_banner_continued()` height consistency** — does it delegate to `add_banner()`? If not, will "Coverage Details (continued)" and "Recommendations (continued)" banners be the same height/width as primary banners?

**b. Page break placement (Premium Summary → Coverage Details)** — The WD_BREAK.PAGE is added as a run in a paragraph. Is there risk of an extra blank page if content overflows naturally?

**c. SIG Disclosure box borders** — Both `set_cell_border()` and `set_no_cell_borders()` may append to `tcBorders`. Verify only the blue left border is visible; top/right/bottom are none.

**d. Recommendations empty-title columns** — `add_two_col_rec_table()` updated to skip `h3` when `title == ''`. Verify Personal Lines right column and Bond right column render without a blank heading row.

**e. Cover letter verbatim copy** — Compare the cover letter copy in the build script against `jay_handoff/proposal.html`. Every sentence must match verbatim. Dynamic tags: only `{memberName}`, `{memberAddress}`, `{quoteDate}`.

### 3. Pages 7–9 Boilerplate Port
Read `jay_handoff/proposal.html` and compare the boilerplate section (pages 7–9) against what's in `build-nbais-wc-template.py`. Verify:
- All section headings present
- All body paragraphs present verbatim
- All bullet lists match (no items added/removed, no text changes)
- No reinterpretation — pure port from HTML

### 4. Global Formatting
Verify the global rules are applied consistently:
- Every table with data rows has vertical centering on cells
- Every table that can span a page break has `set_row_header()` on row 0
- Every blue section title bar uses the same `add_banner()` function
- Cell padding reduced on all data tables (not just some)

---

## Verdict Criteria

| Verdict | When |
|---------|------|
| **PASS** | All code correct, no Critical/Important issues, cover letter tags correct, boilerplate verbatim match |
| **NEEDS-CHANGES** | Critical or Important issues found (wrong tags, missing table headers, incorrect text, border bugs) |
| **FAIL** | Multiple critical issues or systemic pattern of errors |

---

## ADO Tracking (MANDATORY)
After your review, post a comment to ADO#2631:
```bash
mcporter call devops.add_comment project="Legacy Work" id=2631 text="**[Hawkeye — REVIEW cycle 1]**
Verdict: PASS/NEEDS-CHANGES. Cycles: 1. [Summary of findings or 'No issues']."
```

---

## Deliverables
1. Review Report saved to `services/proposal-generator/pipeline/ADO2631-REVIEW-REPORT.md`
2. ADO comment posted
3. Verdict: PASS / NEEDS-CHANGES / FAIL

### Review Report Format
```markdown
# Review Report: ADO#2631
## Verdict: PASS / NEEDS-CHANGES / FAIL
## CC Invocation
[command used]
## Issues Found
### Critical
### Important
### Nitpick
## Edge Case Assessment
[Tony's 5 self-identified risks — each PASS/FAIL with reasoning]
## Boilerplate Fidelity
[pages 7–9 comparison result]
## Consistency Audit
[Global formatting rules check]
## Summary
```
