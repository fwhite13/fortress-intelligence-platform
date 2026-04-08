# Deploy Report — WI #1656 — _hasChanges Change Detection

**Date:** 2026-04-08  
**Deployer:** War Machine (devops subagent)  
**App:** nexus-web  
**Cluster:** fortress-tools-cluster  

---

## What Deployed

- `_hasChanges` computed property in `NewSpecWizard.razor`  
- Confirm step resume notices (Warning/Info MudAlerts)  
- Commits: `fda199c` → HEAD `8ca56d9`

---

## Pre-Deploy Snapshot

| Field | Value |
|-------|-------|
| Task Definition | `nexus-web:18` |
| Running | 1/1 |
| Health | ACTIVE |

---

## Steps Completed

1. ✅ **Pre-deploy snapshot** — `nexus-web:18`, running=1, ACTIVE
2. ✅ **CodeBuild triggered** — `fip-nexus-build:75965298-21fd-4ec9-94b2-b50f8bfdb97e`
   - Build started: 14:08:37 EDT
   - Build completed: 14:10:00 EDT (~1m 23s)
   - Result: **SUCCEEDED**
   - Image digest: `sha256:dd06db436d7f2bf73dbac44012b2fd2fa99120e7247601450216a68621788f03`
   - Image tag: `163f4c3df6bbfe0f47b663ab18b7d28f0dd2b25c`
3. ✅ **Task definition registered** — `nexus-web:19` (image: `nexus-web:latest`)
4. ✅ **ECS service updated** — `force-new-deployment` issued with `nexus-web:19`
   - Deployment ID: `ecs-svc/0405306595603014832`
5. ✅ **ECS stabilized** — running=1, pending=0, single PRIMARY deployment at 14:13:56 EDT
6. ✅ **Health check** — `https://nexus.fortressam.ai/` → HTTP **403** (auth-gated, expected)
7. ✅ **CloudWatch** — Clean startup: EF migrations complete, no errors

---

## Post-Deploy State

| Field | Value |
|-------|-------|
| Task Definition | `nexus-web:19` |
| Running | 1/1 |
| Image Digest | `sha256:dd06db436d7f2bf73dbac44012b2fd2fa99120e7247601450216a68621788f03` |
| Health | HEALTHY (403) |

---

## Rollback Procedure

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:18 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Timeline

| Time (EDT) | Event |
|------------|-------|
| 14:08:16 | Pre-deploy snapshot captured |
| 14:08:37 | CodeBuild started |
| 14:10:00 | CodeBuild SUCCEEDED |
| 14:10:20 | Task def `nexus-web:19` registered |
| 14:10:25 | ECS update-service issued |
| 14:13:56 | ECS STABLE — `nexus-web:19` running=1 |
| 14:14:xx | Health check HTTP 403 ✅ |
| 14:14:xx | CloudWatch clean ✅ |

**Total deploy time:** ~6 minutes
