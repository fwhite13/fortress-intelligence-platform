# DEPLOY Assignment — ADO#2732
## NBAIS WC template v2 — remove empty paragraphs from docx XML

**WI:** ADO#2732 (Legacy Work)
**Commit:** `4abb523`
**Deployment type:** S3 template sync only — NO ECS redeploy needed
**AWS profile:** `fortress-tools-deployer`, region `us-east-1`

---

## Why No ECS Redeploy

The proposal-generator service reads `master.docx` from S3 on every request — there is no in-memory caching of the template. Syncing to S3 is sufficient; the next proposal generation will use the fixed template automatically.

---

## Pre-Deploy Snapshot

```bash
# Confirm current S3 object etag/last-modified before overwriting
aws s3 ls s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx \
  --profile fortress-tools-deployer --region us-east-1
```

Record the current size and last-modified timestamp.

---

## Deploy Step — S3 Sync

```bash
cd /home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/
aws s3 cp master.docx \
  s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx \
  --profile fortress-tools-deployer --region us-east-1
```

Verify upload succeeded:
```bash
aws s3 ls s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx \
  --profile fortress-tools-deployer --region us-east-1
```

Confirm the last-modified timestamp updated and file size matches local (`du -b master.docx`).

---

## Post-Deploy Smoke Test

Run a quick generation test to confirm the service picks up the new template:

```bash
# Call the proposal-generator service via ALB with Host header
curl -s -X POST \
  -H "Host: proposal-generator.dev.fortressam.ai" \
  -H "Content-Type: application/json" \
  -d @/home/fredw/projects/fip/services/proposal-generator/test-payloads/nbais-wc-test.json \
  http://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com/proposals/generate \
  | python3 -m json.tool | head -20
```

Expected: JSON response with `downloadUrl` key. If you get a presigned S3 URL back, the service is using the new template.

If the service returns an error, check CloudWatch:
```bash
aws logs tail /ecs/proposal-generator-dev \
  --since 5m \
  --profile fortress-tools-deployer \
  --region us-east-1 \
  | head -30
```

---

## ADO Comments

**Pre-deploy** (post before S3 sync):
```
**[War Machine — DEPLOY pre-flight]**
Pre-deploy: master.docx in S3 = {size} bytes, last-modified {timestamp}. S3-only deploy — no ECS redeploy needed (service reads template per-request from S3). Syncing now.
```

**Post-deploy** (post after smoke test passes):
```
**[War Machine — DEPLOY]**
S3 sync complete. master.docx updated in s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/. New size: {size} bytes. Smoke test: proposal generation returned downloadUrl ✅. Rollback: re-upload prior master.docx from git at ce8a2b5~1 or a64c6ab.
```

```bash
mcporter call devops.add_comment project="Legacy Work" id=2732 text="**[War Machine — DEPLOY]**\nS3 sync complete. master.docx updated. New size: {size} bytes. Smoke test: downloadUrl returned ✅. No ECS redeploy needed."
```

---

## Rollback Plan

If smoke test fails or service errors after sync:
```bash
# Restore from prior commit (ce8a2b5 had Fix 1 + Fix 4; a64c6ab is the bad commit)
# Best rollback: restore from ce8a2b5 if needed, or re-upload previous S3 version
git show ce8a2b5:services/proposal-generator/templates/verticals/nbais-wc/master.docx \
  > /tmp/master-rollback.docx
aws s3 cp /tmp/master-rollback.docx \
  s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Deliverable

Write deploy report to `services/proposal-generator/pipeline/ADO2732-DEPLOY-REPORT.md`:

```markdown
# Deploy Report — ADO#2732
**Status:** SUCCEEDED / FAILED
**Commit:** 4abb523
**Deploy type:** S3 template sync only

## Pre-deploy snapshot
- S3 object size before: {bytes}
- Last-modified before: {timestamp}

## Deploy
- S3 cp: ✅
- New size: {bytes}
- New last-modified: {timestamp}

## Smoke test
- Generation response: {downloadUrl present / error}
- CloudWatch: {clean / errors}

## Rollback plan
git show ce8a2b5:..../master.docx > /tmp/master-rollback.docx && aws s3 cp ...
```
