# BUILD Assignment: ADO#2709
## Proposal Generator: NBAIS WC Template v2 — Apply Jay Spec Update

---

## Context
- **Working directory:** `/home/fredw/projects/fip/` (monorepo root)
- **Service directory:** `services/proposal-generator/`
- **ADO WI:** #2709 (project: Legacy Work)
- **Commit convention:** `fix(ADO#2709): <description>` (separate commits for v1 preservation and changes)
- **Prior state:** `proposal-generator-dev:31` (commit `97653a1`)
- **Reference files:** `jay_handoff/update/proposal.html` and `jay_handoff/update/SPEC.md` — read these before writing any code

---

## Mandatory: Use Claude Code CLI
Write brief to `/tmp/ado2709-brief.md`, then:
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
cd /home/fredw/projects/fip
cat /tmp/ado2709-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Build Report **MUST** include CC invocation.

---

## Step 1 — Preserve v1 Template (SEPARATE COMMIT FIRST)

**Before any other changes:**
```bash
cd /home/fredw/projects/fip
cp services/proposal-generator/templates/verticals/nbais-wc/master.docx \
   services/proposal-generator/templates/verticals/nbais-wc/master-v1.docx
git add services/proposal-generator/templates/verticals/nbais-wc/master-v1.docx
git commit -m "chore(ADO#2709): preserve master.docx as master-v1.docx before v2 changes"
```

This commit must exist before any template changes are made.

---

## Step 2 — Apply 4 Changes to `build-nbais-wc-template.py`

Read `jay_handoff/update/proposal.html` and `jay_handoff/update/SPEC.md` first to understand the v2.1 spec intent before coding.

### Change 1 — Page 2: Remove Cover Letter Letterhead Block

**Remove from `build_cover_letter_page()` in the build script:**
- The `{memberAddress}` / member address block paragraph(s)
- The "RE: Workers' Compensation Insurance Proposal — Nevada Builders Alliance Member Program" line
- The "Dear {insuredName}," salutation line

**Result:** The cover letter page opens directly with the "About this proposal" heading.

**IMPORTANT:** `{memberAddress}` may still be used in the interior page footer runner — do NOT remove it from there. Only remove from cover letter body content.

**Check `assembleNbaisWcTemplateData.js`:** Ensure `memberAddress` and `insuredName` tags are still passed (they may be used elsewhere). Do not remove them from the data assembly — just remove from the cover letter page template.

### Change 2 — Page 3: Restructure Premium Summary

**In `build_premium_summary_page()` (or equivalent):**

a) **Remove** the "Coverage at a Glance" section entirely — this is the summary box with Insured / WC Statutory / EL Limits. Delete all code that builds this section.

b) **Add "Base Premium" line item** to the premium table:
   - Label: "Base Premium"
   - Value: `{basePremium}` (template tag, formatted as currency in the data layer)
   - Position: before the other premium line items (Estimated Annual Premium, Surplus Contribution, EL Fee, etc.) — or wherever makes logical sense as the base carrier premium

c) **Update Down Payment label** in the premium table:
   - Old format: `{downPayment} (25% — new business). Balance payable online via secure payment link provided upon binding.`
   - New format: `Down Payment Due at Binding (25%): {downPayment}. Balance payable online via secure payment link provided upon binding.`
   - Note: the label text changes; the `{downPayment}` tag position moves to after the colon in the label

### Change 3 — Page 4: Rename + Update Policy Summary

**In `build_coverage_details_page()` or `build_policy_summary_page()`:**

a) **Rename section banner** from "Coverage Details (1 of 2)" → "Policy Summary"

b) **Update interior page footer runner** for this page from "Coverage Details (1 of 2)" → "Policy Summary"
   - Interior page footers are section footers in the docx — find where this page's footer text is set and update it

c) **Add new row** to the Policy Information table:
   - Label: "Delivered By"
   - Value: "Nevada Builders Alliance Insurance Services (NBAIS) via Higginbotham"
   - Position: logical placement in the policy info table (after carrier info rows)

d) **Remove "Financial Strength" row** from the Policy Information table:
   - This was: label "Financial Strength", value "BAWNSIG is a Nevada state-regulated self-insured group. AM Best rating not applicable — see program disclosure."
   - Delete this row entirely

e) **Rename "Coverage and Limits" heading** → "Coverage & Limits"

### Change 4 — Page 5: Rename to Policy Details

**In `build_coverage_details_continued_page()` or equivalent:**

a) **Rename section banner** from "Coverage Details (2 of 2)" → "Policy Details"

b) **Update interior page footer runner** for this page from "Coverage Details (2 of 2)" → "Policy Details"

---

## Step 3 — Update `assembleNbaisWcTemplateData.js`

Check `src/services/assembleTemplateData.js` (or the nbais-wc-specific assembly file) for whether `basePremium` is already passed.

If NOT present, add it to the nbais-wc data assembly:
```javascript
// basePremium = raw carrier premium before surplus contribution and EL fee
basePremium: formatCurrency(payload.quotes[0].premium),
```

Check what `formatCurrency` helper is used in the existing code and use the same one.

Also verify `nbais-wc-test.json` has a `premium` field in `quotes[0]` — if not, add a test value (e.g., `"premium": 12500`).

---

## Step 4 — Test and Sync

```bash
cd /home/fredw/projects/fip

# Rebuild template
python3 services/proposal-generator/scripts/build-nbais-wc-template.py

# Sync master.docx to S3
aws s3 cp services/proposal-generator/templates/verticals/nbais-wc/master.docx \
  s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx \
  --profile fortress-tools-deployer --region us-east-1

# Also sync master-v1.docx for archival
aws s3 cp services/proposal-generator/templates/verticals/nbais-wc/master-v1.docx \
  s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master-v1.docx \
  --profile fortress-tools-deployer --region us-east-1

# Commit all changes
git add -A
git commit -m "fix(ADO#2709): apply Jay v2.1 spec — remove letterhead, restructure premium summary, rename policy pages"
```

---

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| 1 | `master-v1.docx` committed in a separate commit BEFORE any changes |
| 2 | Cover letter: no memberAddress block, no RE line, no Dear salutation — opens with "About this proposal" |
| 3 | Premium Summary: "Coverage at a Glance" section removed |
| 4 | Premium Summary: "Base Premium" line item present with `{basePremium}` tag |
| 5 | Premium Summary: Down Payment label updated to new format |
| 6 | Page 4 banner: "Policy Summary" |
| 7 | Page 4 footer runner: "Policy Summary" |
| 8 | Page 4 table: "Delivered By" row added |
| 9 | Page 4 table: "Financial Strength" row removed |
| 10 | Page 4 heading: "Coverage & Limits" |
| 11 | Page 5 banner: "Policy Details" |
| 12 | Page 5 footer runner: "Policy Details" |
| 13 | `assembleTemplateData.js`: `basePremium` field present |
| 14 | Build runs clean, S3 synced (master.docx + master-v1.docx) |

---

## ADO Tracking
```bash
# After the v1 preservation commit:
mcporter call devops.add_comment project="Legacy Work" id=2709 text="**[Tony Stark — BUILD step 1]**
Commit {hash}: master-v1.docx preservation. v1 archived."

# After all changes committed:
mcporter call devops.add_comment project="Legacy Work" id=2709 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: v2.1 spec changes applied. Build: SUCCEEDED. S3 synced (master.docx + master-v1.docx)."
```

## Deliverables
1. Two commits: v1 preservation + v2 changes
2. Build Report → `services/proposal-generator/pipeline/ADO2709-BUILD-REPORT.md`
3. Both ADO comments posted
