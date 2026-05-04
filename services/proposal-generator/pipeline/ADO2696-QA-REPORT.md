# QA Report: ADO#2696

---

## ✅ FINAL QA — 2026-05-01 20:52 EDT

### Verdict: ✅ PASS

**Service:** `proposal-generator-dev:30`, image `8db3a0a`  
**All 4 test cases passed.** The S3 template upload fix resolved the signature table column width bug.

| Test | Result | Details |
|------|--------|---------|
| TC1 — Health endpoint | ✅ PASS | HTTP 200 |
| TC2 — Generate + download | ✅ PASS | 420 KB DOCX via S3 pre-signed URL |
| TC3 — Signature table tblGrid | ✅ PASS | `['2340', '7020']` ← matches expected |
| TC4 — Document integrity | ✅ PASS | Sections: 8, Tables: 24, Paras: 58 |

**TC3 detail:** Table idx 12, tblGrid `['2340', '7020']`, tcW `['2340', '7020']` — the narrow label column and wide content column are correct.  
**No warnings.** ADO#2696 acceptance criterion met.

---

## ❌ INITIAL QA — 2026-05-01 20:29 EDT (FAIL — stale S3 template)

### Verdict: ❌ FAIL

**Reason:** Signature table column widths still read `['4680', '4680']` (50/50). The template fix landed correctly in the local `master.docx` but was **not synced to S3** before deploy. The service is loading the stale template from S3.

---

### Environment
- **Target:** `https://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com` (Host: `proposal-generator.dev.fortressam.ai`)
- **Task def:** `proposal-generator-dev:29` (commit `01a5860`)
- **Test Start:** 2026-05-01 20:29 EDT
- **Test Duration:** ~3 min

---

### Smoke Tests

| Test | Result | Details |
|------|--------|---------|
| TC1 — Health endpoint | ✅ PASS | HTTP 200 |
| TC2 — Generate proposal | ✅ PASS | HTTP 200, 430 KB DOCX (via S3 pre-signed URL) |

---

### Targeted Tests

| Test | Result | Details |
|------|--------|---------|
| TC3 — Signature table col widths | ❌ FAIL | Table 12: `['4680', '4680']` — expected `['2340', '7020']` |
| TC4 — Document integrity | ✅ PASS | Sections: 8, Tables: 24, Paras: 58 |

---

### Root Cause

The fix in commit `01a5860` correctly modifies `scripts/build-nbais-wc-template.py` and the script **was run** — the local template at `templates/verticals/nbais-wc/master.docx` (422,440 bytes, modified 20:19) correctly shows `['2340', '7020']` when inspected directly.

However, the **deploy pipeline did not include an S3 sync step** for the updated template. The service fetches templates from S3 at request time:

```
S3 bucket: fortress-tools
S3 key:    fip-proposal-templates/verticals/nbais-wc/master.docx
```

The stale template in S3 (built before the fix) still has both columns at 4680 twips, so every generated document inherits the broken widths.

**Evidence:**
- Local template inspection → `['2340', '7020']` ✅
- Generated DOCX (served from S3 template) → `['4680', '4680']` ❌
- `tblGrid` in generated XML also shows `['4680', '4680']` ❌
- ADO2696 deploy report has no S3 sync step

---

### Fix Required

Upload the corrected template to S3:

```bash
cd /home/fredw/projects/fip/services/proposal-generator
aws s3 cp templates/verticals/nbais-wc/master.docx \
  s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx \
  --profile fortress-tools-deployer --region us-east-1
```

No ECS redeployment needed — the service loads templates dynamically from S3.

---

### Issues Found

#### CRITICAL — Template not synced to S3
- **What:** `master.docx` updated locally by build script but not uploaded to S3
- **Expected:** `col widths = ['2340', '7020']` on all signature table rows
- **Actual:** `col widths = ['4680', '4680']` on all signature table rows
- **Table:** Table 12 (By / Print Name / Title / Date rows)
- **Steps to Reproduce:** Generate any NBAIS WC proposal, inspect Table 12 tcW values

---

### Test Summary
- Total tests: 4
- Passed: 3
- Failed: 1
- Warnings: 0

---

### Recommendations

1. **Upload `master.docx` to S3** (command above) — no redeploy needed
2. **Add S3 sync to the deploy pipeline** for template-only changes — this is the second time a template change missed the S3 sync step (see ADO#2593 completion notes: "build-nbais-wc-template.py has no S3 sync step — manual sync required on template changes")
3. After S3 sync: re-run TC2+TC3 to confirm fix
