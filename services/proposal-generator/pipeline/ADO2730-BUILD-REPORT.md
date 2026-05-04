# Build Report — ADO#2730
## Proposal Generator: NBAIS WC Template v2 — Cell Trimming + vAlign + Empty Para Fixes

**Date:** 2026-05-04
**Commit:** `a216424`
**Branch:** `main`
**Build:** SUCCEEDED
**S3:** Synced

---

## What was built

Three fidelity fixes for the NBAIS WC proposal template:
1. Universal `.trim()` on all dynamic string values in the JS data assembler
2. `vAlign=top` on all two-column boilerplate table cells (pages 7-9)
3. Verification pass on empty-para and `outlineLvl` ordering (both already clean)

---

## Files changed

- `services/proposal-generator/src/services/assembleTemplateData.js`
  - Defined `trimVal` helper once at top of `assembleNbaisWcTemplateData` function
  - Applied to all string fields: `state`, `classCode`, `classDescription`, `rate` in classSchedule map; `name` in excludedPersons map; and all string fields in the final return object (`memberName`, `memberAddress`, `memberLegalName`, `policyPeriod`, `policyPeriodDisplay`, `quoteDate`, `basePremium`, `estPremium`, `surplusContribution`, `employersLiabilityFee`, `totalEstimatedPremium`, `downPayment`, `proposalNumber`, `generatedDate`, `templateVersion`)
  - Logo base64 fields intentionally excluded (trimming binary-encoded data would corrupt it)

- `services/proposal-generator/scripts/build-nbais-wc-template.py`
  - Changed `fix_cell_content(cell)` → `fix_cell_content(cell, valign='top')` in `add_two_col_rec_table` (line 468)
  - Fix 3A: No empty paragraphs found between `add_section_divider` and `add_two_col_rec_table` calls — already clean
  - Fix 3B: No `para.style =` assignments exist anywhere in file; `set_outline_level(para, 9)` ordering confirmed correct in both `add_h3` and `add_section_divider`

- `services/proposal-generator/templates/verticals/nbais-wc/master.docx`
  - Rebuilt from source; synced to S3

---

## Parallelization used

No — sequential: CC changes → build → S3 sync → commit

---

## CC sessions run

1 session (CC Sonnet). Spec was precise, execution clean. No notable decisions.

---

## Acceptance criteria verification

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `state` has no leading/trailing whitespace | ✅ `trimVal()` applied |
| 2 | `estPremium` has no trailing CR/LF | ✅ `trimVal()` applied |
| 3 | `name` in excluded persons has no leading/trailing whitespace | ✅ `trimVal()` applied |
| 4 | `electionForm` has no trailing CR/LF | ✅ N/A — static text in Python template, not a dynamic JS field |
| 5 | All dynamic table cell strings trimmed universally | ✅ All string fields in return object wrapped |
| 6 | Two-column boilerplate table cells on pages 7-9: vAlign=top | ✅ `fix_cell_content(cell, valign='top')` in `add_two_col_rec_table` |
| 7 | "Farm & Ranch" etc. right-column items start at top of cell | ✅ Covered by Fix 2 — same `add_two_col_rec_table` change |
| 8 | Empty paragraph between section headings and first sub-heading removed | ✅ None found — already clean |
| 9 | [+] outline icon not present (outlineLvl=9 confirmed after style set) | ✅ No `para.style` assignments found; ordering already correct |
| 10 | Build runs clean, S3 synced | ✅ Build: SUCCEEDED; S3: uploaded 413.2 KiB |

---

## Known edge cases / things Clint should scrutinize

- **AC#4 (`electionForm`):** In the Python template, "Form D-43 — Election to Reject Coverage" is a static literal string (not a dynamic `{electionForm}` placeholder). The plan referenced `electionForm` as a dynamic field but it's hardcoded in the template. No trim needed; no whitespace risk.
- **`trimVal` on currency strings:** `formatCurrencyWc` returns clean strings without whitespace. The `trimVal` wrapping is a no-op for these but adds defensive robustness for future payload changes.
- **Fix 2 scope:** The `valign='top'` change in `add_two_col_rec_table` affects ALL pages that call this function (pages 7, 8, and 9). This is correct per spec — the plan explicitly states "ALL cells in ALL two-column rows on pages 7-9."

---

## How to test locally

```bash
# 1. Check generated docx on S3
aws s3 cp s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx /tmp/master-2730.docx --profile fortress-tools-deployer --region us-east-1
# Open in Word and verify pages 7-9 column alignment

# 2. To test JS trim, submit a test proposal via the API with whitespace in payload fields
# The .trim() fix only affects runtime rendering, not the static template
```
