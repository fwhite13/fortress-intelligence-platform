# Build Report — ADO#2709

## What was built
Applied Jay's v2.1 spec update to the NBAIS WC proposal template: removed letterhead block from cover letter, restructured the Premium Summary table (removed "Coverage at a Glance", added "Base Premium" line item, updated Down Payment label format), renamed "Coverage Details (1 of 2)" sub-section to "Policy Summary" with table updates (Delivered By row, Financial Strength removed, Coverage & Limits heading), and renamed "Coverage Details (2 of 2)" to "Policy Details".

## Files changed
- `services/proposal-generator/scripts/build-nbais-wc-template.py` — All 4 template changes applied (see below)
- `services/proposal-generator/src/services/assembleTemplateData.js` — Added `basePremium` field to `assembleNbaisWcTemplateData()` return object
- `services/proposal-generator/templates/verticals/nbais-wc/master.docx` — Rebuilt from updated script
- `services/proposal-generator/templates/verticals/nbais-wc/master-v1.docx` — Preserved copy of pre-v2 template

## Changes detail

### Change 1 — Cover Letter (Page 3): Removed letterhead block
- Removed: `{quoteDate}`, `{memberName}`, `{memberAddress}` meta block paragraphs
- Removed: RE: line ("Workers' Compensation Insurance Proposal — Nevada Builders Alliance Member Program")
- Removed: "Dear {memberName}," salutation
- Result: Cover letter opens directly with "About this proposal" heading
- `{memberName}` in footer and `{memberAddress}` in assembleTemplateData NOT removed (still used)

### Change 2 — Premium Summary (Page 4): Restructured table
- Removed: Entire "Coverage at a Glance" table (banner + 9 data rows + total + down payment)
- Added: New 7-row "Premium Summary" table:
  - Banner: "Premium Summary" (navy)
  - Row 1: Base Premium / `{basePremium}`
  - Row 2: Estimated Annual Premium / `{estPremium}`
  - Row 3: Surplus Contribution (8%) / `{surplusContribution}`
  - Row 4: Employers' Liability Fee / `{employersLiabilityFee}`
  - Row 5 (total): Total Estimated Cost / `{totalEstimatedPremium}` (light blue, bold)
  - Row 6 (down payment): "Down Payment Due at Binding (25%): {downPayment}." / "Balance payable online via secure payment link provided upon binding."

### Change 3 — Policy Summary sub-section (Page 4): Renames + table updates
- Sub-banner: "Coverage Details — Workers' Compensation" → "Policy Summary"
- Policy Information table: Removed "Financial Strength" row
- Policy Information table: Added "Delivered By" row → "Nevada Builders Alliance Insurance Services (NBAIS) via Higginbotham"
- Heading: "Coverage and Limits" → "Coverage & Limits"

### Change 4 — Policy Details (Page 5): Renames
- Banner: "Coverage Details (continued)" → "Policy Details"
- Footer runner (s5): "Coverage Details (2 of 2)" → "Policy Details"

### Change 5 — assembleTemplateData.js: Added basePremium
- Added `basePremium: formatCurrencyWc(basePremiumNum)` to `assembleNbaisWcTemplateData()` return
- `basePremium` = same value as `estPremium` (raw carrier premium before surplus + EL fee)

## Parallelization used
No — single CC session, sequential changes across 2 files.

## CC sessions run
1 CC session — `claude --model sonnet --print --dangerously-skip-permissions` via pipe mode.
CC committed with message: `feat(ADO#2709): apply v2.1 spec changes to NBAIS WC template`

## Commits
1. `64050cb` — `chore(ADO#2709): preserve master.docx as master-v1.docx before v2 changes`
2. `16239a5` — `feat(ADO#2709): apply v2.1 spec changes to NBAIS WC template`

## Build run
```
python3 services/proposal-generator/scripts/build-nbais-wc-template.py
Saved: /home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/master.docx
```
Build: **SUCCEEDED** ✓

## S3 sync
- `master.docx` → `s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx` ✓
- `master-v1.docx` → `s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master-v1.docx` ✓

## Acceptance criteria verification
- [x] 1. `master-v1.docx` committed in separate commit before any changes — commit `64050cb` ✓
- [x] 2. Cover letter: no memberAddress block, no RE line, no Dear salutation — opens with "About this proposal" ✓
- [x] 3. Premium Summary: "Coverage at a Glance" section removed ✓
- [x] 4. Premium Summary: "Base Premium" line item present with `{basePremium}` tag ✓
- [x] 5. Premium Summary: Down Payment label updated to new format ✓
- [x] 6. Page 4 banner: "Policy Summary" ✓
- [x] 7. Page 4 footer runner: "Policy Summary" — NOTE: s4 footer is "Premium Summary" for the premium section; the sub-banner within s4 is "Policy Summary". The plan says footer = "Policy Summary" but existing footer was "Premium Summary". See note below.
- [x] 8. Page 4 table: "Delivered By" row added ✓
- [x] 9. Page 4 table: "Financial Strength" row removed ✓
- [x] 10. Page 4 heading: "Coverage & Limits" ✓
- [x] 11. Page 5 banner: "Policy Details" ✓
- [x] 12. Page 5 footer runner: "Policy Details" ✓
- [x] 13. `assembleTemplateData.js`: `basePremium` field present ✓
- [x] 14. Build runs clean, S3 synced (master.docx + master-v1.docx) ✓

### Note on AC #7 (Page 4 footer runner)
The plan says update footer from "Coverage Details (1 of 2)" → "Policy Summary". The current s4 footer was `'Premium Summary'` (not "Coverage Details (1 of 2)"). The sub-banner inside the page was "Coverage Details — Workers' Compensation" which was renamed to "Policy Summary". The s4 section footer `'Premium Summary'` was left unchanged since it correctly describes the section and does not match the "Coverage Details (1 of 2)" string from the spec. **Clint should verify** whether s4 footer should be changed to "Policy Summary" or remain "Premium Summary".

## Known edge cases / things Clint should scrutinize
1. **Page 4 footer**: See AC #7 note above — the s4 footer was `'Premium Summary'`, not "Coverage Details (1 of 2)". It was not changed. Verify intent.
2. **Down Payment row layout**: The down payment row uses the label cell for the payment amount (`{downPayment}`) and the value cell for the balance note. This is a non-standard layout vs the other rows — verify it renders correctly in Word.
3. **basePremium vs estPremium**: Both fields resolve to the same raw carrier premium value (`wcQuote.premium`). The template now shows Base Premium and Estimated Annual Premium as separate rows with the same `{basePremium}` / `{estPremium}` values. Confirm this is intentional per Jay's spec.

## How to test locally
```bash
# Rebuild template
python3 services/proposal-generator/scripts/build-nbais-wc-template.py

# Open master.docx in Word/LibreOffice to visually verify:
# 1. Cover letter page opens with "About this proposal"
# 2. Premium Summary table has 6 data rows + Base Premium row
# 3. Sub-section header is "Policy Summary"
# 4. Policy Information table has "Delivered By" row, no "Financial Strength" row
# 5. Heading reads "Coverage & Limits"
# 6. Page 5 banner and footer both read "Policy Details"
```
