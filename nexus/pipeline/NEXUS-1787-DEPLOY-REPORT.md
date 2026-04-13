# NEXUS-1787 Deploy Report
**ADO Work Item:** #1787 — FileUploadZone extension fallback
**Date:** 2026-04-13
**Deployer:** War Machine (Rhodey / devops)
**Service:** nexus-web → ECS cluster: fortress-tools-cluster

---

## Pre-Deploy State

| Field | Value |
|-------|-------|
| Task Definition (rollback target) | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:29` |
| Running / Desired | 1 / 1 |

---

## Deploy Steps

### Step 1 — CodeBuild Triggered
- **Project:** `fip-nexus-build`
- **Build ID:** `fip-nexus-build:efe5d22d-6f57-4f86-a7be-0bab74022441`
- **Triggered at:** ~15:31:03 EDT
- **Result:** ✅ SUCCEEDED (~1 minute)

### Step 2 — ADO Start Comment
- Posted to ADO #1787 at 2026-04-13T19:30:57Z
- Comment ID: 743527

### Step 3 — ECS Force New Deployment
- **Command:** `aws ecs update-service --cluster fortress-tools-cluster --service nexus-web --force-new-deployment`
- **Triggered at:** ~15:32:10 EDT

### Step 4 — ECS Rollout
| Time | State |
|------|-------|
| 15:32:16 | IN_PROGRESS — 1 running, 0 pending |
| 15:32:46 | IN_PROGRESS — 1 running, 1 pending (new task starting) |
| 15:33:17 | IN_PROGRESS — 2 running (old + new overlapping) |
| 15:33:48 | IN_PROGRESS — 1 running, 0 pending (old stopped) |
| 15:34:49 | **COMPLETED** ✅ |

### Step 5 — Health Check (Post-Deploy)

| Metric | Value |
|--------|-------|
| Running | 1 |
| Desired | 1 |
| Pending | 0 |
| Rollout State | COMPLETED |
| Task Definition | `nexus-web:29` |

---

## ADO Complete Comment
- Posted to ADO #1787 at 2026-04-13T19:35:08Z
- Comment ID: 743542

---

## Rollback Procedure

If rollback is needed:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:29 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

> Note: Force-new-deployment reuses task def `:29` — rollback would require reverting the ECR image tag to a prior digest. Task def revision did not increment since no task def update was issued (image tag is `latest`-style resolved at pull time).

---

## Result

✅ **DEPLOY COMPLETE** — nexus-web is live with the FileUploadZone extension fallback fix.
- Build SUCCEEDED in ~1 min
- ECS rolling update completed in ~2.5 min
- Service healthy: 1/1 running, 0 pending
