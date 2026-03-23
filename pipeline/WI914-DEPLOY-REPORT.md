# Deploy Report: WI#914 — FIRM VPBot HttpClient Fix

**Agent:** War Machine (Rhodey)  
**Date:** 2026-03-20  
**Time:** ~10:58–11:05 EDT

---

## Summary

Deployed FIRM VPBot HttpClient fix to ECS. Single-file change in `Meetings.razor` (commit `486828f`) — HttpClient BaseAddress corrected so VPBot join flow functions properly.

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Pre-deploy baseline | `firm-web:27` |
| Commit deployed | `486828f` |
| CodeBuild project | `fip-firm-build` |
| Build ID | `fip-firm-build:159bae6f-07ad-429b-a9c9-db1c8b2cbf57` |

---

## Deploy Steps

| Step | Status | Notes |
|------|--------|-------|
| ADO pre-deploy comment | ✅ DONE | Comment ID 726827 |
| CodeBuild triggered | ✅ DONE | `fip-firm-build` |
| CodeBuild SUCCEEDED | ✅ DONE | ~1.5 min build time |
| ECS force-new-deployment | ✅ DONE | |
| ECS services-stable | ✅ DONE | |
| Health checks | ✅ PASS | All 200 |

---

## Post-Deploy State

| Item | Value |
|------|-------|
| Active task definition | `firm-web:28` |
| Health endpoint | `200` |
| Root (`/`) | `302` (auth redirect — expected) |
| fip-tokens.css | `200` ✅ |

---

## Rollback Plan

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service firm-web \
  --task-definition firm-web:27 --force-new-deployment --region us-east-1
```

Rollback target: `firm-web:27`

---

## Verdict

✅ **DEPLOY SUCCESSFUL** — `firm-web:28` live. Handing to Natasha for VERIFY.

---

## Re-Deploy (2026-03-20)

### Reason for Re-Deploy
Previous deploys (firm-web:28 via CodeBuild runs 1–2) were using a stale commit and failed the WI914 acceptance criteria. Git push issue was resolved and HEAD confirmed as `d40a8ac` (WI914+WI918 changes) before this run.

**Additional blocker encountered:** Docker Hub anonymous pull rate limit (429 Too Many Requests) caused the first two re-deploy attempts to fail during `docker build`. Root cause: `Dockerfile.debian` was pulling `debian:bookworm-slim` from Docker Hub without authentication — CodeBuild IPs hit the unauthenticated rate limit.

### Fix Applied
- Created ECR mirror: `742932328420.dkr.ecr.us-east-1.amazonaws.com/debian:bookworm-slim`
- Pulled `debian:bookworm-slim` locally (SteamServer, authenticated) and pushed to ECR
- Updated `firm/Dockerfile.debian` to use `ARG DEBIAN_IMAGE` defaulting to ECR mirror for both build and final stages
- Committed as `8bfa2c1` — "WI914: Dockerfile.debian — use ECR mirror for debian:bookworm-slim to fix Docker Hub rate limit in CodeBuild"
- Pushed to `origin/main`

### Pre-Deploy Check
```
✅ HEAD matches origin/main (8bfa2c1)
✅ Safe to trigger CodeBuild
```

### Build Results
| Field | Value |
|-------|-------|
| CodeBuild Run | `fip-firm-build:417835e5-340b-499d-b2c1-a3d20ebfa29f` |
| Status | **SUCCEEDED** |
| Built Commit | `8bfa2c10aacf` |
| Previous Failed Runs | `fip-firm-build:8281c2e2` (Docker Hub 429), `fip-firm-build:9f864116` (Docker Hub 429) |

### ECS Outcome
| Field | Value |
|-------|-------|
| Task Definition | `firm-web:28` |
| Running / Desired | 1 / 1 |
| ECS Status | Stable |

### Health Checks
| Endpoint | Status |
|----------|--------|
| `/health` | `200` ✅ |
| `fip-tokens.css` | `200` ✅ |

### Rollback Plan
```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service firm-web \
  --task-definition firm-web:27 --force-new-deployment --region us-east-1
```

### Verdict
✅ **RE-DEPLOY SUCCESSFUL** — `firm-web:28` live, built from `8bfa2c10aacf`. Ready for Fred manual test.
