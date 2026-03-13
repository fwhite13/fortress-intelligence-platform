# Deploy Report: FAIT-DEVOPS-TOOLRESULT-FIX

**Date:** 2026-03-12 (Thu)
**Time:** 22:53–23:00 EDT
**Deployer:** War Machine (Rhodey) — devops subagent
**Pipeline Manager:** Maria Hill

---

## Summary

Deployed FAIT commit `81f2827` (orphaned tool_result guard in BuildConverseMessages) plus
bundled commits `ce830be`, `0b51d7a`, `9359b54`, `6e93b67`, `0e3fb22` (DevOps/Settings/M365 fixes).

---

## Stage 1: CodeBuild

| Field | Value |
|-------|-------|
| **Project** | `fip-fait-build` |
| **Build ID** | `fip-fait-build:c0bdba3b-3e65-41ef-9235-d54742c1c98e` |
| **Status** | ✅ SUCCEEDED |
| **Duration** | ~3.5 minutes (22:53:49 → 22:57:15) |

---

## Stage 2: ECR Image Verification

| Field | Value |
|-------|-------|
| **Repository** | `fred-chat` |
| **Tag** | `kb-latest` |
| **New ECR Digest** | `sha256:28a583bd7a17a9244254e2457a1437ee804f1f2cdbb4bbcbaa9fb365720bcb44` |
| **Image URI** | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:kb-latest` |
| **Status** | ✅ Image confirmed in ECR before ECS update |

---

## Stage 3: ECS Deployment

| Field | Value |
|-------|-------|
| **Cluster** | `fortress-tools-cluster` |
| **Service** | `fred-dev` |
| **Task Definition** | `fred-dev:67` (floating tag — no new task def registered) |
| **Method** | `--force-new-deployment` |
| **Rollout State** | ✅ COMPLETED |
| **Final Running Count** | 1 |
| **Rollout Duration** | ~2 minutes (22:57:39 → 22:59:47) |

**Rollout timeline:**
```
22:57:39 state=IN_PROGRESS running=0
22:58:01 state=IN_PROGRESS running=0
22:58:22 state=IN_PROGRESS running=1
22:58:43 state=IN_PROGRESS running=1
22:59:04 state=IN_PROGRESS running=1
22:59:25 state=IN_PROGRESS running=1
22:59:47 state=COMPLETED   running=1  ✅
```

---

## Stage 4: Digest Verification

| Field | Value |
|-------|-------|
| **Task ARN** | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/752be1b1216f48c7a282791a4cb45541` |
| **Task Digest** | `sha256:28a583bd7a17a9244254e2457a1437ee804f1f2cdbb4bbcbaa9fb365720bcb44` |
| **ECR Digest** | `sha256:28a583bd7a17a9244254e2457a1437ee804f1f2cdbb4bbcbaa9fb365720bcb44` |
| **Match** | ✅ DIGEST MATCH |

---

## Stage 5: Health Check

| Field | Value |
|-------|-------|
| **URL** | `https://fait.dev.fortressam.ai/health` |
| **Response** | `{"status":"healthy","service":"fred","timestamp":"2026-03-13T02:59:54.2778122Z"}` |
| **Status** | ✅ HEALTHY |

---

## Rollback Target

If rollback is needed:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer

aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:67 \
  --region us-east-1 --profile fortress-tools-deployer
```

> **Note:** `fred-dev:67` is the pre-deploy baseline (digest `sha256:77049bed…`).
> Task def `:66` had a broken ECR image URI — do NOT use it.

---

## Commits Deployed

| Commit | Description |
|--------|-------------|
| `81f2827` | Orphaned tool_result guard in BuildConverseMessages |
| `ce830be` | DevOps seed ON DUPLICATE KEY UPDATE fix |
| `0b51d7a` | Settings DevOps dedup |
| `9359b54` | M365 redirect URI from config |
| `6e93b67` | TenantId trailing slash |
| `0e3fb22` | DevOps DDL collation |

---

## Outcome

✅ **DEPLOYMENT SUCCESSFUL** — All stages passed. No rollback required.

- Total time: ~6 minutes (22:53 → 22:59 EDT)
- Service healthy at `https://fait.dev.fortressam.ai/health`
- Digest confirmed matching between ECR and running task
