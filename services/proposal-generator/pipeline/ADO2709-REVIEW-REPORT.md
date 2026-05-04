# Review Report — ADO#2709

**Task:** NBAIS WC Template v2.1 — Jay Spec Update  
**Commits reviewed:** `64050cb` (v1 preservation) + `16239a5` (v2.1 spec changes)  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-04  

---

### Verdict: NEEDS-CHANGES

---

### CC Review Summary

CC (`claude --model sonnet`) reviewed `build-nbais-wc-template.py` and `assembleTemplateData.js` in full. CC caught both issues confirmed below. One CC flag (page 4 footer) was elevated to Critical because it directly violates AC #7. The cover letter bullet stale label is confirmed as Important — no section named "Coverage at a Glance" exists anymore, so the cover letter would reference a phantom section.

One CC concern dismissed as false positive: `insuredName` not present in `assembleNbaisWcTemplateData()` — no template tag references `{insuredName}` in this vertical, and the spec does not require it.

---

### Spec Compliance Check

**Brief:** `services/proposal-generator/pipeline/ADO2709-PLAN.md`

**§ Codebase Map:**
- `build-nbais-wc-template.py` — ✅ modified as specified
- `assembleTemplateData.js` — ✅ modified as specified
- `master-v1.docx` — ✅ created in separate commit before changes
- `master.docx` — ✅ rebuilt

**§ Out of Scope:**
- ✅ No out-of-scope changes detected

**§ Acceptance Criteria:**

| # | Criterion | Status |
|---|-----------|--------|
| 1 | master-v1.docx committed in separate commit BEFORE changes | ✅ Verified — commit `64050cb` |
| 2 | Cover letter: no memberAddress, RE line, Dear salutation; opens with "About this proposal" | ✅ Verified — line 795 opens with `add_h3(doc, 'About this proposal')` |
| 3 | Premium Summary: "Coverage at a Glance" section removed | ✅ Verified — no standalone section exists |
| 4 | Premium Summary: "Base Premium" line item with `{basePremium}` tag | ✅ Verified — line 861 |
| 5 | Premium Summary: Down Payment label updated to new format | ✅ Verified — lines 930–937 |
| 6 | Page 4 banner: "Policy Summary" | ✅ Verified — `add_banner_continued(doc, 'Policy Summary', font_size=13)` at line 955 |
| 7 | Page 4 footer runner: "Policy Summary" | ❌ **FAILED** — `build_standard_footer(s4, 'Premium Summary')` at line ~1680; says "Premium Summary" for both the Premium Summary and Policy Summary halves of Section 4 |
| 8 | Page 4 table: "Delivered By" row added with correct value | ✅ Verified — `'Nevada Builders Alliance Insurance Services (NBAIS) via Higginbotham'` |
| 9 | Page 4 table: "Financial Strength" row removed | ✅ Verified — absent from pi_rows |
| 10 | Page 4 heading: "Coverage & Limits" | ✅ Verified — `add_h3(doc, 'Coverage & Limits')` |
| 11 | Page 5 banner: "Policy Details" | ✅ Verified — `add_banner_continued(doc, 'Policy Details', font_size=13)` |
| 12 | Page 5 footer runner: "Policy Details" | ✅ Verified — `build_standard_footer(s5, 'Policy Details')` |
| 13 | assembleTemplateData.js: `basePremium` field present | ✅ Verified — line 186, `basePremium: formatCurrencyWc(basePremiumNum)` |
| 14 | Build runs clean, S3 synced | ✅ Noted in Tony's build report |

**Spec compliance verdict:** ❌ NON-COMPLIANT — AC #7 not met (blocks PASS)

---

### Consistency Audit

**Files cross-referenced:**
- `build-nbais-wc-template.py` section footer assignments (lines ~1670–1690) ↔ AC #7 spec requirement → ❌ Mismatch: `s4` footer reads `'Premium Summary'`, not `'Policy Summary'`
- `assembleTemplateData.js` `assembleNbaisWcTemplateData()` return value ↔ template tags used in build script → ✅ `basePremium`, `memberAddress`, `downPayment`, `memberName` all consistent
- Cover letter "What is included" bullet (line 832) ↔ actual sections produced by the script → ⚠️ Bullet references "Coverage at a Glance" section which no longer exists

---

### Critical Issues [1]

#### C1: Page 4 footer runner still says "Premium Summary" — AC #7 violation
- **File:** `services/proposal-generator/scripts/build-nbais-wc-template.py` (~line 1680)
- **Category:** Spec non-compliance
- **Issue:** Section 4 (`s4`) contains both the Premium Summary page and the Policy Summary page (separated by a `WD_BREAK.PAGE`, not a section break). The footer is set once for the whole section as `'Premium Summary'`. This means the Policy Summary half of the section also displays "Premium Summary" in its footer runner. AC #7 explicitly requires the Page 4 footer runner to say `"Policy Summary"`.
- **Evidence:**
  ```python
  build_standard_footer(s4, 'Premium Summary')   # ~line 1680
  build_premium_summary_page(doc)
  # ... premium summary content ...
  # ... page break at line ~951 ...
  # ... Policy Summary content in same section ...
  ```
- **Impact:** Deployed document shows wrong footer on the Policy Summary page. AC #7 is unmet.
- **Fix:** Split Section 4 into two Word sections — one for Premium Summary, one for Policy Summary:
  ```python
  # Section 4: Premium Summary
  s4 = doc.add_section(WD_SECTION.NEW_PAGE)
  apply_standard_margins(s4)
  link_header(s4)
  build_standard_footer(s4, 'Premium Summary')
  build_premium_summary_page(doc)   # ends before the page break / Policy Summary content

  # Section 4b: Policy Summary (new section — no page break needed, NEW_PAGE handles it)
  s4b = doc.add_section(WD_SECTION.NEW_PAGE)
  apply_standard_margins(s4b)
  link_header(s4b)
  build_standard_footer(s4b, 'Policy Summary')
  build_policy_summary_section(doc)  # extract the Policy Summary block into its own helper
  ```
  The explicit `WD_BREAK.PAGE` currently separating the two halves can be removed once the section break takes over page pagination.

---

### Important Issues [1]

#### I1: Cover letter "What is included" bullet references removed section name
- **File:** `services/proposal-generator/scripts/build-nbais-wc-template.py` (line 832)
- **Category:** Consistency — stale label
- **Issue:** The cover letter "What is included" list still references `'Premium Summary & Coverage at a Glance'` as the bold lead. "Coverage at a Glance" was removed as a section in this spec update. The document would tell the reader to expect a "Coverage at a Glance" section that no longer exists.
- **Evidence:**
  ```python
  add_bullet(doc, ' — a summary of your proposed coverage terms and estimated premium.',
             lead_bold='Premium Summary & Coverage at a Glance')   # line 832
  ```
- **Fix:**
  ```diff
  - lead_bold='Premium Summary & Coverage at a Glance'
  + lead_bold='Premium Summary'
  ```

---

### Nitpicks [0]

None.

---

### Positive Observations

- v1 preservation was handled correctly in a clean separate commit before any template changes — good discipline.
- All other page changes (cover letter body, base premium, down payment format, banner labels, delivered-by row, financial-strength removal, coverage & limits heading, page 5 banner + footer) are cleanly implemented and match spec.
- `basePremium` sourcing logic is solid: `wcQuote.premium ?? attrs.estimated_premium` with proper fallback, formatted through the same `formatCurrencyWc` helper used for all other currency fields.
- `memberAddress` correctly preserved in data assembly — spec's IMPORTANT note was respected.

---

### What to Fix (NEEDS-CHANGES)

**Fix 1 (Critical — C1): Split Section 4 into Premium Summary + Policy Summary sections**

In `build-nbais-wc-template.py`, the document assembly block currently sets one footer for all of Section 4. The Policy Summary sub-section needs its own Word section with `'Policy Summary'` as the footer runner.

Approach: extract the Policy Summary content (from the existing page-break point forward) into its own section. Remove the manual `WD_BREAK.PAGE` — the new section boundary handles pagination.

**Fix 2 (Important — I1): Update cover letter bullet label at line 832**

```diff
- lead_bold='Premium Summary & Coverage at a Glance'
+ lead_bold='Premium Summary'
```

Both fixes are isolated, low-risk, and require no schema or data layer changes. Once applied, rebuild and re-sync master.docx to S3.
