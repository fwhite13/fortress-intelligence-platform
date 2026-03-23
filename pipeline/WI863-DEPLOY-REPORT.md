# Deploy Report: WI#863 — FAIT Developer KB Wiring
**Date:** 2026-03-20
**Deployer:** War Machine (Rhodey)
**Environment:** fred-dev (ECS, fortress-tools-cluster)

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Task def (before) | `fred-dev:118` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:kb-latest` |
| fip HEAD (pre-deploy) | `721820a` (KbTier.Developer backend changes) |
| DevKbId present | ❌ No |

---

## Part 1: Frontend — fait-for-excel dist copy

**Action:** Built fait-for-excel frontend and copied dist to wwwroot/excel-addin before CodeBuild.

| Item | Detail |
|------|--------|
| Build result | ✅ Success |
| New bundle | `taskpane-zmp9uZHv.js` (305.80 kB) |
| Target path | `fait/src/FortressAI.Web/wwwroot/excel-addin/` |
| Committed | `7524453` — "WI#863: Update FAIT taskpane frontend dist (Developer KB wiring)" |
| Pushed to | `origin/main` |

**Rationale:** FAIT Dockerfile bakes wwwroot into the image at build time (no npm step in buildspec). Frontend dist must be committed before CodeBuild trigger.

---

## Part 2: Backend — CodeBuild fip-fait-build

| Item | Value |
|------|-------|
| Build project | `fip-fait-build` |
| Build ID | `fip-fait-build:9bc47131-773b-4203-a7e1-5af5885afa77` |
| Status | ✅ SUCCEEDED |
| Resolved commit | `75244531489f` (`7524453` — frontend commit, includes `721820a` backend) |
| ECR tag | `kb-latest` |

---

## Part 3: ECS Task Definition Update

| Item | Value |
|------|-------|
| Previous revision | `fred-dev:118` |
| New revision | `fred-dev:119` |
| `KnowledgeBase__DevKbId` | `EE1X6QJ9WH` ✅ |
| `KnowledgeBase__DevDataSourceId` | `CWZRCFWDEV` ✅ |
| Force deploy | ✅ Submitted |
| Service stable | ✅ Yes |

---

## Health Checks

| Check | Result |
|-------|--------|
| `https://fait.dev.fortressam.ai/health` | ✅ HTTP 200 |
| `https://fait.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css` | ✅ HTTP 200 |

---

## Rollback Command

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:118 --force-new-deployment --region us-east-1
```

---

## Summary

Deploy complete. New Docker image (commit `7524453`) is live on fred-dev with:
- Developer KB env vars (`KnowledgeBase__DevKbId`, `KnowledgeBase__DevDataSourceId`) injected
- Updated FAIT taskpane frontend bundle (`taskpane-zmp9uZHv.js`)
- Backend `KbTier.Developer` wiring from commit `721820a`

**Handing to Natasha for QA.**
