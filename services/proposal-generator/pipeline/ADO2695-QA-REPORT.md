# QA Report: ADO#2695
**Analyst:** Natasha Romanoff (Black Widow)
**Verdict:** ⚠️ WARN
**Commit:** `64e2dcd` | **Service:** `proposal-generator-dev:28`
**Test Date:** 2026-05-01 20:16 EDT
**Test Duration:** ~5 minutes

---

## Environment
- **Target:** `https://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com` (Host: `proposal-generator.dev.fortressam.ai`)
- **Payload:** `nbais-wc-test.json`
- **Output:** Downloaded from presigned S3 URL → 430 KB valid OOXML docx

---

## Smoke Tests

| Test | Result | Details |
|------|--------|---------|
| TC1 — Health endpoint | ✅ PASS | HTTP 200 |
| TC2 — Proposal generation | ✅ PASS | 430,218 bytes, valid `Microsoft Word 2007+` |

---

## Targeted Tests

### TC3a — Cover Page First-Page Header

| Check | Expected | Actual | Result |
|-------|----------|--------|--------|
| `different_first_page` | `True` | `True` | ✅ PASS |
| Header paragraph text | Empty `[]` | `[]` | ✅ PASS |
| Header table | 1 row × 1 col, no text | 1r × 1c, text=`''` | ✅ PASS |
| Header table cell fill | Navy (`1F3864`) | `1F3864` | ✅ PASS |
| Header distance (`w:header`) | `144` twips (0.1") | `144` twips | ✅ PASS |
| Vertical alignment in header cell | Not explicitly set (vAlign=none acceptable for decorative bar) | `none` | ✅ PASS |

**Summary:** Cover page header is purely decorative — no text, correct navy fill, header distance 0.1" confirmed. ✅

---

### TC3b — Signature Table Column Widths

**Table:** Table 12 in doc (signature block)
**Section content width:** 9360 twips (8.5" page, 1" margins each side = 6.5" content)

| Check | Expected | Actual | Result |
|-------|----------|--------|--------|
| Table total width | 9360 twips | 9360 twips | ✅ PASS |
| Label column (col 0) | 25% = **2340 twips** | **4680 twips** (50%) | ⚠️ WARN |
| Value column (col 1) | 75% = **7020 twips** | **4680 twips** (50%) | ⚠️ WARN |
| Column type | `dxa` | `dxa` | ✅ PASS |

**Summary:** The signature table's label column is 4680 twips (50%) instead of the expected 2340 twips (25%). The value column is similarly 4680 instead of 7020. The **20%→25% label column change did not apply** — the columns are evenly split 50/50. This is a regression against the stated change in ADO#2695.

---

### TC4 — Document Completeness

| Check | Result | Details |
|-------|--------|---------|
| Document not truncated/corrupt | ✅ PASS | Sections: 8, Tables: 24, Paragraphs: 58 |

---

## Issues Found

### ⚠️ WARN — Signature Table Column Widths Not Applied
- **What:** Label column (By/Print Name/Title/Date) is 4680 twips (50%) instead of 2340 twips (25%). Value column is also 4680 twips (50%) instead of 7020 twips (75%).
- **Expected:** Label col = 2340 twips (25% of 9360), Value col = 7020 twips (75% of 9360)
- **Actual:** Both columns = 4680 twips (50/50 split)
- **Impact:** Visual — labels and value fields are equally wide instead of the narrower label / wider value layout
- **Steps to Reproduce:** Generate any proposal → open docx → inspect Table 12 column widths in signature block

---

## Test Summary

| TC | Test | Result |
|----|------|--------|
| TC1 | Health endpoint | ✅ PASS |
| TC2 | Proposal generation | ✅ PASS |
| TC3a | Cover header text removed | ✅ PASS |
| TC3a | Header distance 0.1" | ✅ PASS |
| TC3a | Navy decorative bar (fill `1F3864`) | ✅ PASS |
| TC3b | Sig table label col 25% | ⚠️ WARN |
| TC3b | Sig table value col 75% | ⚠️ WARN |
| TC4 | Document not truncated | ✅ PASS |

- **Total:** 8 tests
- **Passed:** 6
- **Warnings:** 2
- **Failed:** 0

---

## Verdict

**⚠️ WARN** — Core generation is healthy. The cover page header change (TC3a) is fully correct — no text, correct navy fill, 0.1" header distance. However, the signature table column width change (20%→25% label) did not apply — both columns remain 50/50 (4680/4680 twips). No rollback required; functionality works. Follow-up fix needed for the column proportion.
