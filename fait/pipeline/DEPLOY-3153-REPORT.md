# Deploy Report: ADO#3153 — UserProvisioningService S3 Workspace Seeding

**Deployed by:** Rhodey (DevOps)  
**Date:** 2026-05-09  
**Time:** ~19:34–19:50 EDT  

---

## Deployment Type
ECS task definition update — new Docker image build + ECR push + ECS service update

## Pre-Deploy Snapshot
- **Previous image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:b3d571b7`
- **Previous task def:** `fred-dev:145`
- **Tip commit deployed:** `61b4ec75`
- **Service state:** 1 running, 0 pending, 1 desired (ACTIVE)

## Steps Completed

1. ✅ **Pre-deploy snapshot captured** — fred-dev:145, fred-chat:b3d571b7, 1 running
2. ✅ **Docker build** — `docker build --no-cache -f fait/Dockerfile.debian` from `/home/fredw/projects/fip`
   - Image: `fred-chat:61b4ec75`
   - Digest: `sha256:6da73f51987bac8db408cf7fa6a548312153cd49496044d52190a7f6285047e0`
   - Build used `Dockerfile.debian` (WSL2 / MCR-free)
3. ✅ **ECR push** — `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:61b4ec75`
4. ✅ **Task def registered** — `fred-dev:146`
   - `Fargate__ContainerName = fait-v2-agent-harness` ✅ preserved
   - `taskRoleArn = arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` ✅ preserved
   - All env vars from fred-dev:145 preserved exactly
5. ✅ **ECS service updated** — `fred-dev` → `fred-dev:146` with `--force-new-deployment`
6. ✅ **Service stabilized** — `aws ecs wait services-stable` returned STABLE
7. ✅ **CloudWatch logs verified** — clean startup, no errors
   - `Application started. Press Ctrl+C to shut down.`
   - `Now listening on: http://[::]:8080`
   - Database initialization complete
   - MCP tools responding (devops, brave, m365 — all 200 OK)
8. ✅ **ADO#3153 updated** — state → Resolved

## Deployment Result

| Item | Value |
|------|-------|
| Image | `fred-chat:61b4ec75` |
| ECR URI | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:61b4ec75` |
| Task Definition | `fred-dev:146` |
| Service | `fred-dev` on `fortress-tools-cluster` |
| Running | 1 |
| Pending | 0 |
| Desired | 1 |
| Status | ACTIVE / HEALTHY |

## What's Live

**UserProvisioningService** with atomic S3 workspace seeding on wizard completion:
- Commit `61b4ec75`: rollback on AccessDenied — matches generic exception handler
- Commit `81075e5a`: UserProvisioningService with atomic S3 workspace seeding

S3 files seeded on wizard completion:
- `workspaces/{userId}/assistants/SOUL.md`
- `workspaces/{userId}/assistants/USER.md`
- `workspaces/{userId}/assistants/AGENTS.md`
- `workspaces/{userId}/memory/MEMORY.md`

## Rollback Plan

### Pre-Deploy State
- Previous image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:b3d571b7`
- Previous task def: `fred-dev:145`

### Rollback Commands
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:145 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

### Rollback SLA
< 5 minutes (ECS)

## Cost Impact
No change — same Fargate resources (1 vCPU, 2GB RAM).

## Lessons Learned
- Pre-flight script `deploy.sh fait` checks for ECR repo `fortress-ai-chat` (old name) — should be updated to check `fred-chat`. Pre-flight warning was a false alarm; actual ECR repo `fred-chat` confirmed healthy.
- fip-deploy.sh / fip-build.sh wrapper scripts still reference old workspace path (`/home/fredw/.openclaw/workspace/fortress-ai-chat`) and ECR repo (`fortress-ai-chat`) — these scripts are outdated for fred-chat/fait. Manual build procedure from monorepo root is the correct path until scripts are updated.
