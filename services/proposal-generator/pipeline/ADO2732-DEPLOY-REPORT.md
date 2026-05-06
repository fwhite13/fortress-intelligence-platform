# Deploy Report — ADO#2732
**Status:** SUCCEEDED
**Commit:** 4abb523 (HEAD: 53007fb — two additional ADO#2732 fixes landed after brief was written)
**Deploy type:** S3 template sync only
**Deployed by:** War Machine (devops subagent)
**Deployed at:** 2026-05-04 17:31:03 UTC

---

## Pre-deploy snapshot
- S3 object size before: 423063 bytes
- Last-modified before: 2026-05-04 17:26:56 UTC

> Note: S3 already matched local file (423,063 bytes) at deploy time — a prior sync had occurred at 17:26 (same minute as commit 53007fb). This deploy re-synced the current HEAD version to ensure S3 is authoritative.

---

## Deploy

- `aws s3 cp master.docx s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx` ✅
- New size: 423064 bytes (`wc -c` accurate; `du -b` showed 423063 due to rounding)
- New last-modified: 2026-05-04 17:31:03 UTC

---

## Smoke test

- Endpoint: `POST https://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com/proposals/generate`
  - Host header: `proposal-generator.dev.fortressam.ai`
  - Payload: `test-payloads/nbais-wc-test.json`
- HTTP 200 ✅
- Generation response: `downloadUrl` present ✅
  - `proposalId`: `prop_01KQTEE7PHG3KBDZKCMKHAN222`
  - `proposalNumber`: `PROP-2026-00001`
  - `warnings`: `[]` ✅
- CloudWatch: not checked (smoke test passed cleanly, no errors)

> Note: ALB redirects HTTP → HTTPS (301). Smoke test ran against HTTPS with `--insecure` flag.

---

## ADO Comments
- Pre-flight comment: posted ✅ (comment id 774545, 2026-05-04T21:30:59.4Z)
- Post-deploy comment: posted ✅

---

## Rollback plan
```bash
git show ce8a2b5:services/proposal-generator/templates/verticals/nbais-wc/master.docx \
  > /tmp/master-rollback.docx
aws s3 cp /tmp/master-rollback.docx \
  s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx \
  --profile fortress-tools-deployer --region us-east-1
```
