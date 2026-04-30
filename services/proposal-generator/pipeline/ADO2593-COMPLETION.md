# Pipeline Completion: ADO#2593

## Outcome: DEPLOYED ✅

**WI:** ADO#2593 — Proposal Generator: NBAIS WC Word template + test payload
**Deployed:** proposal-generator-dev:24 (commit a078f36)
**Date:** 2026-04-30

## Pipeline Summary
- **BUILD:** 2 cycles (Tony) — master.docx via python-docx with real Word headers/footers, assembleTemplateData nbais-wc branch, dual logo loading in documentRenderer.js, test payload. Cycle 2: EL fee constant $120→$20.
- **REVIEW:** 2 cycles (Clint PASS) — identified EL fee bug cycle 1; cycle 2 clean sign-off
- **DEPLOY:** 2 cycles (Rhodey) — cycle 1 stale ECR layer (Docker cache issue); cycle 2 clean --no-cache rebuild + commit SHA tag
- **QA:** FAIL then PASS — cycle 1 merge fields empty (stale image); cycle 2 all TCs passed

## Artifacts
- pipeline/ADO2593-STATE.md
- pipeline/ADO2593-REVIEW-BRIEF.md
- pipeline/ADO2593-REVIEW-REPORT.md
- pipeline/ADO2593-DEPLOY-REPORT.md
- pipeline/ADO2593-QA-REPORT.md
- pipeline/ADO2593-COMPLETION.md (this file)

## Follow-up Items
1. test-payloads/nbais-wc-test.json: excludedPersons should be string array, not object array
2. TC5 expected values in QA docs need updating (basePremium = quotes[0].premium not class sum)
3. build-nbais-wc-template.py has no S3 sync step — manual sync required on template changes
