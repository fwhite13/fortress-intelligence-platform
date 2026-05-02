# REVIEW Assignment: ADO#2632
## Proposal Generator: NBAIS WC Template — Remaining Fidelity Issues

---

## Context
- **ADO WI:** #2632 (Legacy Work project)
- **Review Cycle:** 1 of 2
- **Commit:** `dd7052e` — fix(ADO#2632): cover header/footer sections, logo aspect ratio, column widths, contact box gap, signature lines, callout box border
- **Working directory:** `/home/fredw/projects/fip/` (monorepo root)

---

## Mandatory: Use Claude Code CLI
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cd /home/fredw/projects/fip
cat /tmp/ado2632-review-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Review Report **MUST** include CC invocation.

---

## Files to Review
- `services/proposal-generator/scripts/build-nbais-wc-template.py` — all 9 fixes

## Build Report
`services/proposal-generator/pipeline/ADO2632-BUILD-REPORT.md`

---

## Review Focus

### 🔴 Priority 1 — Logo: Static Image vs Runtime Tag (MUST RESOLVE)
Tony flagged this as needing explicit confirmation. Read the build script and the proposal-generator service code:

1. Check `services/proposal-generator/src/` — look for how `{%stackedLogoBase64}` or logo-related image tags are handled in the docxtemplater rendering pipeline
2. Check if the image module config in the service has a `getSize` or `size` function for the stacked logo tag
3. **If the runtime service expects `{%stackedLogoBase64}` as a docxtemplater tag** (dynamic substitution at runtime):
   - Tony's fix is WRONG — inserting a static image in the build script means the tag is gone from master.docx and the service's image module will break or ignore it
   - The correct fix is to restore `{%stackedLogoBase64}` in master.docx AND fix the `getSize` function in the service to return dimensions that match the natural aspect ratio
   - **Flag as Critical I1**
4. **If the logo is truly static** (not dynamically substituted — same logo always, not per-member):
   - Tony's fix is fine — static image at correct aspect ratio is correct
   - Note as PASS

This is the most important check in this review cycle.

### Issue 1/2 — Word Header/Footer Sections
- Find `build_cover_first_page_header()` and `build_cover_first_page_footer()` in the build script
- Verify `section.different_first_page_header_footer = True` is set
- Verify `is_linked_to_previous = False` on both header and footer
- Verify the original body-content blue bar table and footer block are removed from body content (not just duplicated)
- Verify standard page header is set on s3 and linked to previous on s4–s9 (interior pages need a header too)

### Issue 4 — Prepared For Table Centering
- Verify `META_TABLE_W` is set to a value less than `CONTENT_W` (not full width)
- Verify `WD_TABLE_ALIGNMENT.CENTER` is set
- Check that label alignment was changed from RIGHT → LEFT (or flag if right-aligned is preferred per Jay's reference)

### Issue 5 — Global Vertical Alignment (exhaustive check)
- Count `WD_ALIGN_VERTICAL.CENTER` assignments — Tony reports 33. Spot-check a few table functions:
  - `build_premium_summary_page()` — are all data cells covered?
  - `build_recommendations_1_page()` — two-col rec table cells?
  - `build_next_steps_page()` — sig table, contact boxes?
- Flag any function with data table cells missing vertical alignment

### Issue 6 — Column Widths
- Verify `add_kv_table` default `label_pct` changed from 35 to 30
- Check Coverage & Limits table is NOT affected (Tony noted it was kept at 65/35 — verify this is intentional and correct for that table's long labels)
- Verify signature table: label ~20% (~1872), line ~80% (~7488), sums to CONTENT_W

### Issue 7 — Contact Box Gap (3-column spacer)
- Verify the spacer column is truly invisible: `set_no_cell_borders()` called, no background fill, ~200 twips
- Verify both content columns are sized correctly and sum with spacer to ≤ CONTENT_W

### Issue 9 — Callout Box Coverage
- Tony noted only ONE callout box was found (in `build_employee_benefits_page()`). Check pages 7 and 8 build functions for any other "Discuss with your producer" or similar callout boxes. If present, are they also updated?
- If pages 7–8 have callout boxes that were missed, flag as Important

---

## Verdict Criteria

| Verdict | When |
|---------|------|
| **PASS** | Logo question resolved (static is correct OR dynamic fix applied), all other checks pass |
| **NEEDS-CHANGES** | Logo tag issue confirmed as wrong approach, or other Critical/Important issues |
| **FAIL** | Multiple critical issues |

---

## ADO Tracking
```bash
mcporter call devops.add_comment project="Legacy Work" id=2632 text="**[Hawkeye — REVIEW cycle 1]**
Verdict: {PASS/NEEDS-CHANGES}. Cycles: 1. {summary}."
```

## Deliverables
1. Review Report → `services/proposal-generator/pipeline/ADO2632-REVIEW-REPORT.md`
2. ADO comment posted
3. Clear verdict on the logo tag question — this is blocking
