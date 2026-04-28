# Deploy Report: ADO#2499

**Feature:** Cross-Epic Predecessor Linking in AdoCreationService + StubAdoService
**Commit:** `73dab07` — `feat(nexus#2499): cross-Epic predecessor linking in AdoCreationService + StubAdoService`
**Deployed by:** War Machine (Rhodey) — DevOps subagent
**Date:** 2026-04-28

---

## Deployment Type
AWS ECS (Fargate) — CodeBuild image build + force-new-deployment

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Previous task def | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:46` |
| Previous revision | `nexus-web:46` |
| Previous image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest` |
| Service health | ACTIVE, 1/1 RUNNING, 0 pending, PRIMARY deployment |
| AzureAd env vars | ✅ Present (AzureAd__ClientId, AzureAd__ClientSecret, AzureAd__TenantId — baseline :3+) |

---

## Steps Completed

1. ✅ **Pre-deploy snapshot** — nexus-web:46, 1/1 RUNNING, AzureAd vars present
2. ✅ **CodeBuild triggered** — `fip-nexus-build:a9fca133-45dc-4f15-a934-60c6b9ff40c0`
3. ✅ **Build SUCCEEDED** — ~1.5 min (~11:41–11:42 EDT)
4. ✅ **ECS force-new-deployment** — nexus-web, cluster fortress-tools-cluster
5. ✅ **Service stability wait** — stable by 11:44 EDT
6. ✅ **Health check passed** — 1/1 RUNNING, HEALTHY, rollout COMPLETED
7. ✅ **CloudWatch logs clean** — no ERR entries; only pre-existing HTTPS_PORTS WRN (expected)
8. ✅ **No migration step** — no new EF migrations in this WI (as documented)

---

## Post-Deploy State

| Item | Value |
|------|-------|
| Task def revision | `nexus-web:46` (CodeBuild updates `:latest` tag in-place) |
| Running image digest | `sha256:12c75134564c4ee526811e15fb17f228c7c4370ea0e7dbf026a71d7be948ca84` |
| Service status | ACTIVE |
| Running count | 1/1 |
| Pending count | 0 |
| Rollout state | **COMPLETED** |
| Container health | **HEALTHY** |
| Log stream | `ecs/nexus-web/1d1aeadf5d8c400bafc2528a8dde15d4` |
| New errors in logs | None (pre-existing HTTPS_PORTS WRN only — expected) |

---

## Deployment Time

| Milestone | Time (EDT) |
|-----------|-----------|
| Build triggered | 11:41 |
| Build SUCCEEDED | 11:42 |
| ECS force-new-deployment | 11:43 |
| Service stable | 11:44 |
| Health confirmed HEALTHY | 11:45 |
| **Total duration** | ~4 minutes |

---

## Rollback Plan

### Pre-Deploy State
- Previous task def: `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:46`
- Previous revision: `nexus-web:46`
- Previous image digest: (`:latest` tag — ECR retains previous layer; re-push from prior commit if needed)

### Rollback Commands (copy-paste ready)
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer

# Step 1: Update service back to previous task def
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:46 \
  --region us-east-1

# Step 2: Wait for stability
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services nexus-web \
  --region us-east-1

# Step 3: Verify health
aws ecs describe-services \
  --cluster fortress-tools-cluster \
  --services nexus-web \
  --query 'services[0].{status:status,running:runningCount,desired:desiredCount,deployments:deployments[*].rolloutState}' \
  --output json \
  --region us-east-1
```

> **Note:** Since CodeBuild overwrites `:latest` in ECR, a full rollback to the prior image requires re-triggering a build from the previous commit (`a965b58`) or manually re-tagging in ECR. The task def revision :46 remains valid for rollback of ECS metadata.

### Rollback SLA
- ECS rollback target: **< 5 minutes**

---

## Files Deployed

| File | Change |
|------|--------|
| `Services/StubAdoService.cs` | Modified — batch ordering + two-pass predecessor resolution |
| `Services/AdoCreationService.cs` | Created — Phase 2 placeholder |

---

## Notes
- No EF Core migrations in this WI — migration runner completed with zero pending migrations (confirmed in startup logs)
- Pre-existing `WRN: Overriding HTTP_PORTS 'HTTPS_PORTS'` is normal startup behavior, not new
- Build completed unusually fast (~1.5 min) — CodeBuild cache likely warm

---

_War Machine out. nexus-web is HEALTHY._
