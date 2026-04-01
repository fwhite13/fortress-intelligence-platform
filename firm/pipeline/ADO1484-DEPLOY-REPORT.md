# ADO#1484 Deploy Report — FIRM: Stop Recording Button

**Deployed by:** War Machine (Rhodey)  
**Date:** 2026-04-01  
**Start:** 00:01 EDT  
**End:** 00:13 EDT  
**Duration:** ~12 minutes  

---

## Deployment Type

ECS update — `firm-web` service via CodeBuild `fip-firm-build`  
Plus: `firm-vpbot` task definition stopTimeout update

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| firm-web task def | `firm-web:72` |
| Running image digest | `sha256:e6ca47a58e202efb68b14b147d7ecb9f9f295a0acc559ec4757018223b2c291f` |
| firm-vpbot task def | `firm-vpbot:1` |
| Commit SHA (HEAD) | `f7c4784c9444d070acb92a4befbdbd057321a97d` |

---

## Steps Completed

1. ✅ **Pre-deploy snapshot captured** — firm-web:72, vpbot:1, image digest logged  
2. ✅ **ADO comment posted** — deploy starting notification  
3. ✅ **git push origin main** — pushed 2 commits (f7c4784, 2feea22) to origin  
4. ✅ **CodeBuild triggered** — build ID: `fip-firm-build:44eb5de2-9249-4c52-b339-bce7e7961cab`  
5. ✅ **firm-vpbot stopTimeout update** — registered `firm-vpbot:2` with `stopTimeout: 120` (see note below)  
6. ✅ **CodeBuild SUCCEEDED** — image pushed to ECR  
7. ✅ **firm-web:73 registered** — image: `firm-web:f7c4784c9444d070acb92a4befbdbd057321a97d`  
8. ✅ **ECS service updated** — `--force-new-deployment` with `firm-web:73`  
9. ✅ **Service stabilized** — `rolloutState=COMPLETED`, `running=1`, `desired=1`, `failed=0` (~3 min)  
10. ✅ **Stale target deregistered** — `172.31.69.3` deregistered from `meetings-web-dev-tg`  
11. ✅ **Target group clean** — 1 healthy target: `172.31.75.0:8080`  
12. ✅ **FipShared check** — HTTP 302 (not 404) ✅  
13. ✅ **Health check** — Endpoint responding (Cloudflare proxying)  

---

## New Deployment State

| Item | Value |
|------|-------|
| firm-web task def | `firm-web:73` |
| New image | `firm-web:f7c4784c9444d070acb92a4befbdbd057321a97d` |
| New image digest | `sha256:3cd1f0722a943832ec82bfdb411ce7356e242cb7953e65f480555bd753971c6a` |
| firm-vpbot task def | `firm-vpbot:2` (stopTimeout: 120s) |
| TG active target | `172.31.75.0:8080` |
| CodeBuild ID | `fip-firm-build:44eb5de2-9249-4c52-b339-bce7e7961cab` |

---

## firm-vpbot stopTimeout Update: ⚠️ PARTIAL — FARGATE CONSTRAINT

**Status:** Registered `firm-vpbot:2` with `stopTimeout: 120` (NOT 900)

**Issue:** Clint's request was `stopTimeout = 900` (15 minutes). AWS Fargate enforces a hard cap of **120 seconds** for `stopTimeout`. The attempt to register `firm-vpbot:2` with `stopTimeout: 900` was rejected:

```
ClientException: Tasks using the Fargate launch type must have a container stop timeout of less than 120 seconds.
```

**What was done:** Registered `firm-vpbot:2` with `stopTimeout: 120` (the Fargate maximum — 4x improvement over the default 30s).

**Impact:** The vpbot's post-stop pipeline (WAV upload + Whisper + Bedrock summary) may still be cut short if it takes longer than 120 seconds. On Fargate, 120s is the ceiling.

**⚠️ Follow-up Required (N1 — Fargate Constraint):**  
To achieve a true 15-minute graceful shutdown window, one of the following architectural changes is needed:

1. **Decouple the pipeline from the container lifecycle:** Have `StopTask` only trigger a "stop signal" to the bot, which then finishes its pipeline and exits cleanly _before_ ECS sends SIGTERM. (Preferred — requires bot-side logic change.)
2. **Use ECS on EC2 instead of Fargate:** EC2 launch type has no `stopTimeout` cap. (Not recommended — increases ops burden.)
3. **Move post-stop pipeline to a separate Lambda/Step Function triggered by SNS/SQS:** Bot drops a message to a queue at stop time; pipeline runs independently. (Best long-term solution.)

**This must be discussed with Clint and the software architect before the next FIRM release.**

---

## Rollback Plan — firm-web

If rollback is needed, execute immediately:

```bash
# Step 1 — Revert ECS service to previous task def
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service firm-web \
  --task-definition firm-web:72 \
  --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Step 2 — Wait for stabilization (poll every 15s — do NOT use ecs wait)
# Poll until: rolloutState=COMPLETED, runningCount=desiredCount, failedTasks=0

# Step 3 — Get new task IP and deregister stale targets from meetings-web-dev-tg

# Step 4 — Verify health
curl -sk https://firm.dev.fortressam.ai/health

# Step 5 — Verify FipShared (must not be 404)
curl -sk -o /dev/null -w "%{http_code}" https://firm.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css
```

**Previous image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:9874a850ee55ac86c95070407895653bc994dd77`  
**Previous task def:** `firm-web:72`  
**Rollback SLA:** < 5 minutes

---

## Rollback Plan — firm-vpbot stopTimeout

```bash
# Revert to firm-vpbot:1 (stopTimeout: 30s default)
# No service update needed — vpbot uses RunTask per meeting.
# New meetings will use firm-vpbot:1 if specified explicitly,
# or firm-vpbot:2 will remain active (120s is still better than 30s default).
# No immediate rollback required unless issues observed.
```

---

## Verification

- [x] FipShared CSS: HTTP 302 (not 404) ✅
- [x] Health endpoint: Responding (Cloudflare proxying) ✅
- [x] ECS rolloutState: COMPLETED ✅
- [x] runningCount=desiredCount=1 ✅
- [x] failedTasks=0 ✅
- [x] TG target count: 1 (172.31.75.0:8080) ✅
- [x] Image digest: sha256:3cd1f0722a... confirmed in ECR ✅

---

## ADO Work Items

- **ADO#1484** — FIRM: Stop Recording button  
- vpbot N1 (stopTimeout constraint) — flagged for architectural follow-up

---

_Rhodey / War Machine — shipped it._
