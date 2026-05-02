# QA Report: ADO#2631 — NBAIS WC Template Fidelity Pass

**Analyst:** Natasha Romanoff (Black Widow)  
**Date:** 2026-05-01  
**Test Start:** 15:19 EDT  
**Test End:** 15:32 EDT  
**Duration:** ~13 minutes

---

## Verdict: ✅ PASS

All 7 test cases passed. Document generated successfully with correct structure, content, and formatting.

---

## Environment

- **Service:** `proposal-generator-dev` on `fortress-tools-cluster`
- **Task Def:** `proposal-generator-dev:26`
- **Image:** `fip-proposal-generator:de138c5`
- **ALB:** `https://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com`
- **Host Header:** `proposal-generator.dev.fortressam.ai`
- **Output:** `/tmp/ado2631-qa-proposal.docx` (429,699 bytes, valid OOXML)

---

## Test Cases

| TC | Test | Result | Details |
|----|------|--------|---------|
| TC1 | Service health `/health` | ✅ PASS | HTTP 200 |
| TC2 | Generation succeeds | ✅ PASS | POST returns JSON with signed S3 URL; downloaded 429KB valid .docx |
| TC3 | Cover page title | ✅ PASS | Line break between "Workers' Compensation" and "Insurance Proposal" confirmed |
| TC4 | Cover letter: no section bar, body present | ✅ PASS | No "Cover Letter" section title paragraph; full body copy present (About, Program highlights, etc.) |
| TC5 | Premium Summary: "What's next" + page break | ✅ PASS | "What's next" present (P30); explicit page break (P32) before Coverage Details |
| TC6 | Page structure / not truncated | ✅ PASS | 8 sections, 24 tables, 61 paragraphs — full document present |
| TC7 | Pages 7–9 boilerplate | ✅ PASS | Commercial Lines, Personal Lines, Bond, Group Benefits, Life, Retirement all present |

---

## Detailed Findings

### TC2 — Generation Flow
The service returns a JSON response (not a raw binary stream) containing a pre-signed S3 URL. The .docx is stored at:
```
s3://fortress-tools/proposals/2026/05/prop_01KQJFP4XCB5VW1CZQ29E4B2BZ.docx
```
Document downloaded cleanly at 429,699 bytes. File type confirmed as `Microsoft Word 2007+`.

### TC3 — Cover Page
- P0: `Nevada Builders Alliance Insurance Solutions` (org name)
- P2: `Workers' Compensation\nInsurance Proposal` — line break (`w:br type=line`) between the two lines. ✅

### TC4 — Cover Letter
- No paragraph with text "Cover Letter" found anywhere in the document body (only in page footer of Jay's HTML — correctly omitted). ✅
- Body copy starts at P13 (`Dear Carson Valley Excavation, LLC,`)
- "About this proposal" heading present (P14)
- "Program highlights" heading + 5 bullet points present (P17–P22)
- "What is included in this proposal" heading + 4 bullet points present (P23–P28)

### TC5 — Premium Summary
- "What's next" heading present at P30 (style: Normal) ✅
- Body text at P31: *"Review the Coverage Details on the following page, confirm payroll and class code accuracy..."* ✅
- Explicit page break at P32 (empty paragraph containing `w:br type=page`) — immediately before Coverage Details table ✅

### TC5b — SIG Disclosure (Page 5)
- Section header "Self-Insured Group Disclosure" at P43 ✅
- Disclosure body in single-cell table (body element 54)
- **Blue left border confirmed:** `w:start w:val="single" w:sz="24" w:color="2E75B6"` ✅
- Text matches Jay's HTML verbatim:
  > *"BAWNSIG is a Nevada-regulated self-insured group, not a traditional insurance carrier, and therefore does not carry an AM Best financial strength rating. BAWNSIG operates under the regulatory oversight of the Nevada Division of Industrial Relations and maintains reserves in accordance with state requirements. Members of NBAIS benefit from the group's long-standing solvency and claims-paying history as a construction industry SIG in Nevada."*

### TC5c — Surplus Contribution (Page 4)
Updated text confirmed at P38:
> *"As a self-insured group (SIG), BAWNSIG requires a surplus contribution in addition to the estimated premium. This contribution — calculated at 8% of the estimated premium — is a regulatory requirement for SIG participation in Nevada and supports the financial reserves of the group. It is not a fee retained by NBAIS or your producer."*

### Table Header Repeat
All 4 data tables have `w:tblHeader` on their first row (repeats on page break): ✅
- Table 3: Coverage at a Glance
- Table 6: Coverage / Limit (Coverage Details)
- Table 8: Employee Classification Schedule (State / Class Code / Description / Payroll / Rate / Est. Premium)
- Table 9: Excluded Persons (Name / Election Form)

### TC6 — Next Steps Contact Boxes (Page 6)
- Contact box table at body element 58 ✅
- No outer box border — only an internal center divider (`w:end` and `w:start` with `color="CCCCCC"`) ✅
- Cell 0: `Your NBAIS Producer | Dianne Slater | Account Manager | (775) 555-0100 | dslater@nbais.com`
- Cell 1: `NBAIS Program Office | Nevada Builders Alliance Insurance Services | 1234 Builder's Way...`

### TC7 — Pages 7–9 Boilerplate
Full content present:
- P49: Coverage Recommendations intro text ✅
- P50: Commercial Lines ✅
- P52: Commercial Lines (continued) ✅
- P53: Personal Lines ✅
- P54: Bond Recommendations ✅
- P56: Group benefits intro ✅
- P57: Group Benefits ✅
- P58: Life Department ✅
- P59: Retirement Plan Services ✅
- P60: Producer call-to-action text ✅

---

## Issues Found

None.

---

## Test Summary

| Category | Count |
|----------|-------|
| Total TCs | 7 |
| Passed | 7 |
| Failed | 0 |
| Warnings | 0 |

---

## Artifacts

- Generated proposal: `s3://fortress-tools/proposals/2026/05/prop_01KQJFP4XCB5VW1CZQ29E4B2BZ.docx`
- Local copy: `/tmp/ado2631-qa-proposal.docx`
- Reference: `jay_handoff/proposal.html` — text matches on all verified sections

---

_Trust nothing. Verify everything._  
_— Natasha Romanoff, QA Analyst_
