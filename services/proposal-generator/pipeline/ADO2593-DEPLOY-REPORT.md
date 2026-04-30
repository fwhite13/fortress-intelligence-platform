# ADO#2593 — Proposal Generator: NBAIS WC Word Template + Test Payload
## Deploy Report

**Date:** 2026-04-30  
**Deployer:** War Machine (Rhodey)  
**ADO Work Item:** #2593  
**Status:** ✅ SUCCEEDED

---

## What Shipped

**Commits:** da247a0, d6e2327, 515c39d

| File | Change |
|------|--------|
| `templates/verticals/nbais-wc/master.docx` | New NBAIS WC Word template |
| `templates/verticals/nbais-wc/meta.json` | Template metadata |
| `templates/verticals/nbais-wc/logo_horizontal.png` | Brand logo (horizontal) |
| `templates/verticals/nbais-wc/logo_stacked.png` | Brand logo (stacked) |
| `src/services/assembleTemplateData.js` | Added `assembleNbaisWcTemplateData` + EL fee fix |
| `src/services/documentRenderer.js` | Added `loadNamedLogos` + `isNbaisWc` branch |
| `test-payloads/nbais-wc-test.json` | New test payload |
| `scripts/build-nbais-wc-template.py` | Build helper script |

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Service | `proposal-generator-dev` |
| Task definition | `proposal-generator-dev:22` |
| Image (pre) | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator:430e06d` |
| Running/Desired | 1/1 |
| Deployment ID (prev) | `ecs-svc/3637705073603183635` |
| Snapshot time | 2026-04-30 13:22 EDT |

---

## Step 2 — S3 Sync Results

**Command:**
```bash
aws s3 sync templates/verticals/nbais-wc/ s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/ \
  --profile fortress-tools-deployer --region us-east-1 --exact-timestamps
```

**Files confirmed in S3:**
| File | Size | Timestamp |
|------|------|-----------|
| `logo_horizontal.png` | 147,142 bytes | 2026-04-30 13:08:40 |
| `logo_stacked.png` | 234,658 bytes | 2026-04-30 13:08:40 |
| `master.docx` | 194,574 bytes | 2026-04-30 13:08:40 |
| `meta.json` | 413 bytes | 2026-04-30 13:08:40 |

✅ All 4 files verified in `s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/`

---

## Step 3 — Docker Build

**Command:**
```bash
cd ~/projects/fip
docker build --no-cache -t fip-proposal-generator:latest -f services/proposal-generator/Dockerfile .
```

**Result:** ✅ SUCCESS  
**Image digest (local):** `sha256:5302974764b5bfe757bdce44e027c5797a77563d98717875ac2d93a05e012002`

---

## Step 4 — ECR Push

**Repository:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator`  
**Tag:** `latest`  
**ECR digest:** `sha256:9a85cf7b54729fc3b3751efee0f717aaf20059f531b87d4c0c315f29c43b097c`  
**Result:** ✅ SUCCESS (most layers already existed)

---

## Step 5 — Task Definition Registration

| Item | Value |
|------|-------|
| New task definition | `proposal-generator-dev:23` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator:latest` |
| Updated from | `proposal-generator-dev:22` (image tag `430e06d`) |

---

## Step 6 — ECS Service Update

**Command:**
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition proposal-generator-dev:23 \
  --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

**New deployment ID:** `ecs-svc/4802179973757977356`  
**Result:** ✅ Service updated

---

## Step 7 — Health Check Results

**Stabilization timeline:**
| Time | Status |
|------|--------|
| 13:24:38 | PRIMARY running=0, old ACTIVE running=1 (new task starting) |
| 13:25:08 | PRIMARY pending=1 (new task launched) |
| 13:25:39 | PRIMARY running=1, old ACTIVE running=1 (rolling) |
| 13:26:10 | PRIMARY running=1, old DRAINING running=0 |
| 13:27:11 | PRIMARY running=1, single deployment ✅ |

**CloudWatch logs:** `/ecs/proposal-generator-dev`  
**Health endpoint:** `/health` → HTTP 200 ✅  
**New task ID:** `d48453ff1e7a48a48b36b65fb80926a4`  
**Startup errors:** None

---

## Rollback Plan

If issues arise, roll back to previous task definition:

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition proposal-generator-dev:22 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

**Note:** S3 template assets do not need to be reverted for rollback — the previous task definition will simply not use the NBAIS WC template route.

---

## ADO Comments

| Comment | ID | Posted |
|---------|----|--------|
| Pre-deploy snapshot | 767651 | 2026-04-30T17:22:25Z |
| Deploy complete | 767658 | 2026-04-30T17:27:26Z |

---

## Summary

| Item | Value |
|------|-------|
| Previous task def | `proposal-generator-dev:22` |
| New task def | `proposal-generator-dev:23` |
| Previous image | `fip-proposal-generator:430e06d` |
| New image digest | `sha256:9a85cf7b54729fc3b3751efee0f717aaf20059f531b87d4c0c315f29c43b097c` |
| S3 sync | ✅ 4/4 files |
| Build | ✅ Clean |
| ECR push | ✅ |
| ECS service | ✅ 1/1 healthy |
| Health checks | ✅ HTTP 200 |
| Deploy duration | ~5 minutes |
| Ready for QA | ✅ |

---

_War Machine out._

---

## Cycle 2 — Clean Rebuild (2026-04-30)

**Reason:** Stale ECR image from Docker layer cache. Previous deployment (cycle 1, task def :23) had cached layers that did not include the isNbaisWc branch code fully. Full --no-cache rebuild required.

### Pre-Build Verification
- ✅ `isNbaisWc` check found at line 128 of `documentRenderer.js`
- ✅ `assembleNbaisWcTemplateData` import found at line 10
- ✅ `assembleNbaisWcTemplateData` call found at line 142
- Commit SHA: `a078f36`

### Build
- ✅ `docker build --no-cache` from monorepo root
- Build time: ~275s (full layer rebuild, no cache)
- Image: `fip-proposal-generator:latest`

### ECR Push
- ✅ Pushed `:latest` → `sha256:a365b55a995eb9dc99546d39f3537ed1e3d866b1733f26c3f89f94679fd23d42`
- ✅ Pushed `:a078f36` → same digest
- ECR repo: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator`

### Task Definition
- Cloned from `proposal-generator-dev:23`
- Updated image to `fip-proposal-generator:a078f36` (commit SHA tag, not :latest)
- Registered as `proposal-generator-dev:24`
- Rollback: `proposal-generator-dev:22`

### ECS Deploy
- Service: `proposal-generator-dev` on `fortress-tools-cluster`
- Forced new deployment with task def `:24`
- Rollout: `COMPLETED`
- Running: 1/1, failedTasks: 0

### Health Verification
- ✅ CloudWatch `/health` → HTTP 200 confirmed
- No startup errors in logs

### ADO Comments
- Pre-deploy: Comment ID 767693
- Post-deploy: Comment ID 767703

## Summary (Cycle 2)

| Item | Value |
|------|-------|
| Previous task def | `proposal-generator-dev:23` |
| New task def | `proposal-generator-dev:24` |
| Commit SHA | `a078f36` |
| New image tag | `fip-proposal-generator:a078f36` |
| Image digest | `sha256:a365b55a995eb9dc99546d39f3537ed1e3d866b1733f26c3f89f94679fd23d42` |
| Build type | Clean --no-cache |
| ECR push | ✅ :latest + :a078f36 |
| ECS service | ✅ 1/1 healthy, COMPLETED |
| Health checks | ✅ HTTP 200 |
| Ready for QA | ✅ TC4+TC5 |

---

_War Machine out. (Cycle 2)_
