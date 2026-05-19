# Deploy Report: ADO#3525 — PROD INCIDENT Clean Rebuild

**Date:** 2026-05-19  
**Deployed by:** Rhodey (DevOps subagent)  
**Status:** ✅ COMPLETE — Service HEALTHY  

---

## Summary

fait-prod was running contaminated evolution/harness code (`fait-prod:49`, image `5b393b39`). 
Rollback to `:45` failed (ECR image `3b7177b4` deleted). 
Built a clean image from commit `c3914307a26c0f3c0ef9e0039009129964f237f5` (last v1 commit before harness code), 
pushed to ECR, registered new task def `fait-prod:50`, deployed, verified HEALTHY.

---

## Pre-Deploy State

| Field | Value |
|-------|-------|
| ECS service task def | `fait-prod:45` (set but unresolvable — ECR image gone) |
| Actually running | `fait-prod:49` — image `5b393b39` (contaminated evolution code) |
| Service status | ACTIVE, Desired=1, Running=1 (running wrong image) |
| Impact | Rob Nethery and users hitting assistant setup spinner |

---

## Build

| Field | Value |
|-------|-------|
| Source commit | `c3914307a26c0f3c0ef9e0039009129964f237f5` |
| Commit message | `fix(fait#3123): migration — add EF attributes, INFORMATION_SCHEMA guards, char(36) FK fix` |
| Dockerfile used | `Dockerfile.debian` (MCR blocked on WSL2; debian base from ECR mirror) |
| Build method | Local Docker (`docker buildx build --builder mcr-builder`) — NOT CodeBuild |
| Build flags | `--no-cache` |
| Build context | `/home/fredw/projects/fip/` (monorepo root) |
| Build time | ~2 min |

---

## ECR Image

| Field | Value |
|-------|-------|
| Repository | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat` |
| Tag | `fait-prod-v1-stable` |
| **Digest** | `sha256:64b5f511be6cc5a9a20af3a44c849e4200a58621a2412de42dbd2cde22615f4e` |
| Size | 100.8 MB |
| Pushed at | 2026-05-19T10:26:35 EDT |

---

## ECS Deployment

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `fait-prod` |
| **New task def** | `fait-prod:50` |
| Task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-prod:50` |
| Env vars source | Copied exactly from `fait-prod:45` (36 env vars) |
| CPU / Memory | 1024 / 2048 |
| Deploy method | `--force-new-deployment` |
| Deployed at | 2026-05-19 ~10:27 EDT |
| Stabilized at | 2026-05-19 ~10:29 EDT |

---

## Post-Deploy Verification

| Check | Result |
|-------|--------|
| ECS service deployments | Single PRIMARY deployment only (`:49` drained) |
| Task last status | RUNNING |
| Container health | HEALTHY (ECS health check passing) |
| Desired count | 1 |
| Running count | 1 |
| Pending count | 0 |
| Task def running | `fait-prod:50` ✅ |
| ECR digest match | sha256:64b5f511... matches push digest ✅ |

**Note:** `curl -I https://fait.fortressam.ai` returns HTTP 403 from Cloudflare bot challenge 
(expected for curl/non-browser user agents). ECS healthcheck at `http://localhost:8080/health` 
is passing (HEALTHY status confirmed via `describe-tasks`).

---

## Rollback Instructions

If this deploy needs to be reverted:

1. The same ECR image (`fred-chat:fait-prod-v1-stable`) remains in ECR — it will not expire.
2. `fait-prod:50` itself is the rollback target if anything newer goes wrong.
3. To roll forward to a new clean build: rebuild from a clean commit and register `:51`.
4. **Do NOT redeploy `:49`, `:48`, `:47`, or `:46`** — those are all contaminated with harness/evolution code.
5. **Do NOT use CodeBuild** for fait-prod deploys until a prod-only pipeline is set up from a release branch.

Emergency rollback command:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-prod \
  --task-definition fait-prod:50 \
  --force-new-deployment \
  --region us-east-1
```

---

## Root Cause (for follow-up)

CodeBuild `fip-fait-build` builds from `refs/heads/master`. Evolution/harness code was merged to `main` 
(which is the same as master in this repo). Normal pipeline deploys from master → contaminated prod.

**Required follow-up (separate WI):** Create a prod-only build pipeline that builds from a `release/fait-prod` 
branch with explicit promotion. CodeBuild must never auto-deploy to `fait-prod` from `master`.

---

## ADO

- WI #3525 marked Active: ✅ (before build started)  
- WI #3525 marked Done: (pending this report delivery to pipeline-manager)
