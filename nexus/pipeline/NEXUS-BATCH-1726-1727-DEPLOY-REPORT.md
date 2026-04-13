# NEXUS Deploy Report — ADOs #1726 + #1727

**Date:** 2026-04-13  
**Deployed by:** War Machine (devops subagent)  
**ADOs:** FAIT #1726, FAIT #1727  
**Service:** `nexus-web` on ECS cluster `fortress-tools-cluster`

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Previous task definition | `nexus-web:29` |
| Previous image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest` |

---

## Deploy Steps

### Step 1 — CodeBuild Triggered
- **Project:** `fip-nexus-build`
- **Build ID:** `fip-nexus-build:1a0b2c41-fb96-4ac6-bf82-42a278086c46`
- **Triggered at:** ~14:25 EDT
- **Final status:** ✅ SUCCEEDED (at ~14:42 EDT, ~17 minutes)

### Step 2 — ADO Start Comments
- ✅ Comment posted on FAIT #1726 (comment ID: 743383)
- ✅ Comment posted on FAIT #1727 (comment ID: 743384)

### Step 3 — ECS Force New Deployment
- **Command:** `aws ecs update-service --cluster fortress-tools-cluster --service nexus-web --force-new-deployment`
- **Task definition:** `nexus-web:29` (unchanged — force-deploy picks up new image)
- **Triggered at:** ~14:42 EDT

### Step 4 — ECS Health Check
- **Rollout state:** ✅ COMPLETED
- **Running / Desired:** 1 / 1
- **Pending:** 0
- **Stabilized at:** ~14:44 EDT

### Step 5 — ADO Complete Comments
- ✅ Comment posted on FAIT #1726 (comment ID: 743415)
- ✅ Comment posted on FAIT #1727 (comment ID: 743417)

---

## Summary

| Phase | Status | Time |
|-------|--------|------|
| Pre-deploy capture | ✅ | 14:24 |
| CodeBuild triggered | ✅ | 14:25 |
| CodeBuild SUCCEEDED | ✅ | 14:42 |
| ECS force-deploy | ✅ | 14:42 |
| ECS rollout COMPLETED | ✅ | 14:44 |
| ADO comments (start) | ✅ | 14:25 |
| ADO comments (complete) | ✅ | 14:44 |

**Total deploy time:** ~20 minutes

---

## Rollback Procedure

If rollback is needed, run:

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:29 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

> Note: `nexus-web:29` was the task definition in use before this deploy. Since this was a force-new-deployment (same task def), rollback means re-deploying the prior image by pinning the old task definition explicitly.

---

## No Issues

Deploy completed cleanly. No rollback required. All health checks passed.
