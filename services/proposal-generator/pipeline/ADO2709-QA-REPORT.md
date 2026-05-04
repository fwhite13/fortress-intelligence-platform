# QA Report: ADO#2709

## Verdict: ✅ PASS

**Tester:** Natasha Romanoff (Black Widow — QA Analyst)
**Service:** `proposal-generator-dev:32` (image `acf9a25`)
**ALB:** `https://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com`
**Host Header:** `proposal-generator.dev.fortressam.ai`
**Test Date:** 2026-05-04
**Test Duration:** ~3 minutes

---

### Change Summary (Jay v2.1 spec — NBAIS WC template)
1. Cover letter: letterhead block removed (memberAddress, RE line, Dear salutation) — opens with "About this proposal"
2. Premium Summary: Coverage at a Glance removed; Base Premium line added with `{basePremium}`; Down Payment label updated
3. Page 4 "Policy Summary": banner renamed; "Delivered By" row added; "Financial Strength" row removed; "Coverage & Limits" heading; footer runner = "Policy Summary" (own section)
4. Page 5 "Policy Details": banner + footer runner renamed

---

## Test Results

### TC1 — Health Check
| Test | Result | Details |
|------|--------|---------|
| `GET /health` | ✅ PASS | HTTP 200 |

---

### TC2 — Generate + Download
| Test | Result | Details |
|------|--------|---------|
| POST `/proposals/generate` (nbais-wc) | ✅ PASS | HTTP 200, `proposalId` returned, no warnings |
| DOCX download from S3 | ✅ PASS | 433,518 bytes (423KB) — valid DOCX |

**Note:** Test payload already contained `premium: 14850.00` — no modification needed.

---

### TC3 — Structural Checks
| Check | Result | Details |
|-------|--------|---------|
| Section count | ✅ PASS | 9 sections (Policy Summary now its own section confirmed) |
| Footer: Section 0 | ✅ PASS | (blank — cover/title) |
| Footer: Section 1 | ✅ PASS | `…Cover Letter` |
| Footer: Section 2 | ✅ PASS | `…Premium Summary` |
| Footer: Section 3 | ✅ PASS | `…Policy Summary` ← renamed correctly |
| Footer: Section 4 | ✅ PASS | `…Policy Details` ← renamed correctly |
| Footer: Section 5 | ✅ PASS | `…Next Steps & Authorization` |
| Footer: Sections 6–8 | ✅ PASS | `…Coverage Recommendations (1-3 of 3)` |
| Table count | ✅ PASS | 24 tables |

---

### TC4 — Content Spot-Checks
| Check | Result | Details |
|-------|--------|---------|
| letterhead REMOVED (`Dear`) | ✅ PASS | Not found (absent as expected) |
| RE line REMOVED (`RE: Workers`) | ✅ PASS | Not found (absent as expected) |
| About this proposal present | ✅ PASS | Found |
| Base Premium tag resolved | ✅ PASS | `{basePremium}` resolved to `$14,850.00` — Table 2 Row 1 confirmed |
| Down Payment new format | ✅ PASS | `Down Payment Due at Binding` found |
| Coverage at a Glance REMOVED | ✅ PASS | Not found (absent as expected) |
| Policy Summary banner | ✅ PASS | Found |
| Delivered By row (`Nevada Builders Alliance Insurance Services`) | ✅ PASS | Found |
| Financial Strength REMOVED | ✅ PASS | Not found (absent as expected) |
| Coverage & Limits heading | ✅ PASS | Found |
| Policy Details banner | ✅ PASS | Found |

**Result: 11/11 checks green**

**Investigation note on `basePremium` check:** The TC4 script checked for the literal string `basePremium` expecting it to be present (as an unresolved tag marker). However, the tag was correctly substituted — Premium Summary table shows `Base Premium | $14,850.00`. The template is functioning as intended; this is a PASS, not a failure.

---

### TC5 — Document Integrity
| Check | Result | Details |
|-------|--------|---------|
| Sections | ✅ PASS | 9 |
| Tables | ✅ PASS | 24 |
| Paragraphs | ✅ PASS | 52 |

---

### Premium Summary Table (Verified)
| Line Item | Value |
|-----------|-------|
| Base Premium | $14,850.00 |
| Estimated Annual Premium | $14,850.00 |
| Surplus Contribution (8%) | $1,188.00 |
| Employers' Liability Fee | $120.00 |
| Total Estimated Cost | $16,158.00 |
| Down Payment Due at Binding (25%) | $4,039.50 |

---

## Test Summary
| Category | Total | Passed | Failed | Warnings |
|----------|-------|--------|--------|----------|
| TC1 Health | 1 | 1 | 0 | 0 |
| TC2 Generate/Download | 2 | 2 | 0 | 0 |
| TC3 Structure | 11 | 11 | 0 | 0 |
| TC4 Content | 11 | 11 | 0 | 0 |
| TC5 Integrity | 3 | 3 | 0 | 0 |
| **Total** | **28** | **28** | **0** | **0** |

---

## Issues Found
None.

---

## Final Verdict: ✅ PASS

All Jay v2.1 spec changes confirmed in the generated DOCX. Letterhead removed, banners renamed, Coverage at a Glance removed, Base Premium resolved correctly, Delivered By row present, Financial Strength removed, Policy Summary and Policy Details sections properly named with correct footer runners. Document is structurally sound with 9 sections and 24 tables.
