# Deploy Report: ADO#1483 — FIRM Bot meeting-end detection + callback resilience

**Deployed by:** War Machine (Rhodey)  
**Date:** 2026-03-31 / 2026-04-01 (EDT)  
**Deployment type:** ECS task def update — firm-web service (CodeBuild triggered)

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Task def revision | `firm-web:70` |
| Task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:70` |
| Running task | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/3e0482300707441cb866539221028c34` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:7491cb9` |
| Image digest | `sha256:a47028ba82e2f33343fff6eda35a5e74bc70f6926f09bea02a1f792648a33287` |
| Health baseline | 403 (Cloudflare auth — expected) |

---

## Git Push

```
To github.com:fwhite13/fortress-intelligence-platform.git
   0c068c5..9874a85  main -> main
```
- HEAD commit: `9874a850ee55ac86c95070407895653bc994dd77` (short: `9874a85`)
- Top commit: `fix(ADO#1483): fix stale 60s log message in meeting-bot.ts (N3)`

---

## CodeBuild

| Item | Value |
|------|-------|
| Build ID | `fip-firm-build:55beecc0-f20c-4784-b059-a3bd476a093e` |
| Status | **SUCCEEDED** |
| Duration | ~90 seconds |

**Note:** CodeBuild pushed image tagged with full 40-char commit SHA:  
`742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:9874a850ee55ac86c95070407895653bc994dd77`  
(NOT the 7-char short hash `9874a85` — initial task def :71 used short hash and failed with CannotPullContainerError; corrected to full SHA in task def :72)

---

## New Image URI

```
742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:9874a850ee55ac86c95070407895653bc994dd77
```
- Image digest: `sha256:e6ca47a58e202efb68b14b147d7ecb9f9f295a0acc559ec4757018223b2c291f`

---

## New Task Definition

| Item | Value |
|------|-------|
| New revision | `firm-web:72` |
| New task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:72` |
| Deregistered | `firm-web:71` (bad task def with short SHA image — CannotPullContainerError) |

---

## Rollback Plan

### Pre-Deploy State
- Task def: `firm-web:70`
- Image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:7491cb9`
- Image digest: `sha256:a47028ba82e2f33343fff6eda35a5e74bc70f6926f09bea02a1f792648a33287`

### Rollback Commands (copy-paste ready)
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_DEFAULT_REGION=us-east-1

aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service firm-web \
  --task-definition firm-web:70 \
  --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Poll stabilization (do NOT use ecs wait)
# Then re-deregister stale TG targets, confirm TG count = 1

# Rollback health verify
curl -sk -o /dev/null -w "%{http_code}" https://firm.dev.fortressam.ai/health
curl -sk -L -o /dev/null -w "%{http_code}" https://firm.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css
```

### Rollback SLA
- ECS: < 5 minutes

---

## Stabilization Results

| Time | rolloutState | running | desired | failedTasks |
|------|-------------|---------|---------|-------------|
| 0s | IN_PROGRESS | 1 | 1 | 0 |
| 15s–165s | IN_PROGRESS | 1–2 | 1 | 0 |
| 180s | **COMPLETED** | 1 | 1 | 0 |

**Running task:** `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/b1500330b58240f4a2cdf1760bb91a8f`  
**Private IP:** `172.31.69.3`

**Note:** First deployment (task def :71) had 1 failedTask at 210s due to short SHA image tag. Corrected and redeployed with task def :72 using full SHA. Second deployment stabilized cleanly.

---

## Target Group Cleanup

**Target group:** `meetings-web-dev-tg` (`arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/meetings-web-dev-tg/7a7e9af531f05a53`)

| Action | Target IP | Port | Result |
|--------|-----------|------|--------|
| Retained (new) | `172.31.69.3` | 8080 | `healthy` |
| Deregistered (stale) | `172.31.74.16` | 8080 | Drained → removed |

**Final TG count: 1** ✅ (`172.31.69.3:8080 — healthy`)

Drain timeout: 300s (completed fully ~5 min after deregistration)

---

## FipShared Check

```bash
curl -sk -L -o /dev/null -w "%{http_code}" \
  https://firm.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css
```
**Result: 200** ✅ (302 redirect through Cloudflare → 200 on static asset)

---

## Final Health Check

```bash
curl -sk https://firm.dev.fortressam.ai/health
```
**Result:** Cloudflare challenge response (same as pre-deploy baseline — CF auth protecting endpoint)  
**Status code baseline:** 403 (CF auth) → consistent with pre-deploy ✅

Service is live and routing correctly through ALB → CF → ECS.

---

## Deployment Summary

| Step | Status | Notes |
|------|--------|-------|
| Pre-deploy snapshot | ✅ | task def :70, digest sha256:a470... |
| ADO comment (starting) | ✅ | ID 734949 |
| git push | ✅ | 0c068c5..9874a85 |
| CodeBuild trigger | ✅ | fip-firm-build:55beecc0 |
| CodeBuild SUCCEEDED | ✅ | ~90s |
| Task def :71 registered | ❌ | Short SHA tag — CannotPullContainerError |
| Task def :72 registered | ✅ | Full SHA tag — correct |
| ECS service update → :72 | ✅ | force-new-deployment |
| ECS stabilization | ✅ | COMPLETED 180s, 0 failed |
| TG stale target deregistered | ✅ | 172.31.74.16 removed, TG=1 |
| FipShared check | ✅ | 200 OK |
| Health check | ✅ | CF auth (expected baseline) |

---

## Lesson Learned

**CodeBuild tags images with the full 40-char commit SHA**, not the 7-char short hash.  
Always use the full SHA when referencing CodeBuild-pushed images in task definitions.  
Pattern: `firm-web:{full-40-char-sha}` not `firm-web:{short-7-char-sha}`

---

_Report written by War Machine — 2026-04-01_
