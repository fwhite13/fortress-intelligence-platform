# ADO#4248 — Deploy Report
## CC Agent Avatar During Task Execution

**Date:** 2026-05-27  
**Deployer:** devops subagent (rhodey-ado4248)  
**Status:** ✅ DEPLOY COMPLETE

---

## Summary

Deployed Blazor-only UI change (ADO#4248) to `fred-dev` ECS service. No harness rebuild required — single-image deploy of `fred-chat` only.

---

## What Was Deployed

- **Commit:** `5534de9c` — `fix(fait#4248): add font-size to cc-icon to match fa-tasks size`
- **File Changed:** `fait/src/FortressAI.Web/Components/Chat/ChatView.razor`
  - SmartToy MudIcon replaces spinning gear emoji for CC task chips
  - `.chat-task-indicator__cc-icon` CSS: width/height/color + `font-size: 0.875rem`
  - `.cc-agent-icon--pulse` animation for active state
  - Header badge: SmartToy + pulse when `_ccTaskActive`

---

## Deployment Details

| Item | Value |
|------|-------|
| ECR Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:5534de9c` |
| Image Digest | `sha256:dbb5f323e691e0271b920659f78cbe54ec71699db510980b85be48df5d5b53d7` |
| Pre-deploy Task Def | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:288` |
| New Task Def | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:289` |
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `fred-dev` |
| Running / Desired | 1 / 1 |
| Rollout State | `COMPLETED` |
| Deploy Time | ~2026-05-27 13:21–13:23 EDT |

---

## Pre-flight Notes

- Docker credential helper (`desktop.exe`) not present in WSL2 — fixed by clearing `credsStore` from `~/.docker/config.json` and re-authenticating to ECR. This is a known WSL2 limitation.
- Repo had a new commit (`12378215`, ADO#4249) pushed between build start and push. Built and deployed the correct ADO#4248 commit (`5534de9c`) explicitly.

---

## Rollback Plan

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:288 \
  --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer
```

---

## Post-Deploy

- ✅ ECS service stable (rollout COMPLETED)
- ✅ 1/1 tasks running
- ✅ ADO#4248 updated to Resolved
- ⬜ QA validation pending (returning to Maria)
