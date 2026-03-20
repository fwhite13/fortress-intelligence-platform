# Deploy Report: WI908 — @rendermode Hotfix

## Outcome: ✅ DEPLOYED — SITE RESTORED

| Field | Value |
|-------|-------|
| Work Item | WI908 |
| Commit | `6b5d91b` |
| Fix | `@rendermode` directive — resolves 500 for all users |
| CodeBuild Project | `fip-famos-build` |
| Build ID | `fip-famos-build:613cf0cd-0705-4753-a66c-e013dc1c9416` |
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `famos-dev` |
| Task Definition | `famos-dev:3` (force-deployed new image under same task def) |
| Deployed By | `fortress-tools-deployer` |
| Deploy Time | 2026-03-19 20:39–20:41 EDT |

---

## Pre-Deploy State

| Check | Status |
|-------|--------|
| Site root | 500 (broken) |
| CodeBuild rollback available | `famos-dev:3` (previous broken task) |

---

## Build Summary

| Phase | Result | Time |
|-------|--------|------|
| CodeBuild started | ✅ | 20:39:22 |
| CodeBuild completed | ✅ SUCCEEDED | 20:41:55 |
| ECS force-new-deployment | ✅ Completed | 20:40:44 |
| Service steady state | ✅ running=1 desired=1 | 20:41 |

**Build mechanism:** CodeBuild pushed new Docker image to ECR (`famos-web:latest`) and triggered a force-new-deployment on the ECS service. Task cycled: old task stopped, new task started at `20:40:44`.

---

## Post-Deploy Verification

| Check | Expected | Actual | Pass? |
|-------|----------|--------|-------|
| Root (`/`) | 200 or 302 (NOT 500) | **200** | ✅ |
| Health (`/health`) | 200 | **200** | ✅ |
| Blazor (`/_blazor`) | 302 | **302** | ✅ |
| FIP tokens CSS | 200 | **200** | ✅ |
| Task status | RUNNING | **RUNNING** | ✅ |

**All pass criteria met. Site restored.**

---

## Rollback Plan

If regression is detected post-deploy:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer

# Force rollback to previous ECR image (re-tag previous image as latest, then force-deploy)
# OR update service to a known-good task definition revision:
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service famos-dev \
  --task-definition famos-dev:3 \
  --force-new-deployment \
  --region us-east-1

# Monitor rollback
aws ecs wait services-stable --cluster fortress-tools-cluster --services famos-dev --region us-east-1
```

> Note: Current task def is `:3`. If issues arise, previous known-good ECR image can be re-tagged as `latest` and force-deployed.

---

## Timeline

| Time (EDT) | Event |
|------------|-------|
| 20:39:16 | ADO comment posted — DEPLOY STARTING |
| 20:39:22 | CodeBuild `fip-famos-build` started |
| 20:40:44 | New ECS task started (new image) |
| 20:41:24 | Deploy triggered (ECS force-new-deployment complete) |
| 20:41:55 | CodeBuild SUCCEEDED |
| 20:42 | Site verification: all checks pass |

**Total deploy time: ~3 minutes**

---

*Deployed by War Machine (James Rhodes) — devops agent*  
*Pipeline: WI908 HOTFIX track*
