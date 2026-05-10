# Deploy Report: ADO#3190 — 4.3-B: Memory ZIP Export

**Date:** 2026-05-10  
**Agent:** War Machine (James Rhodes) — DevOps  
**Status:** ✅ COMPLETE

---

## Summary

Deployed ADO#3190 (FAIT Epic 4 final WI — memory ZIP export) to `fred-dev` ECS service.

---

## What Was Deployed

- **Commit:** `0c113528` — feat(fait#3190): add ZIP export endpoint and Export button on Memory page
- **Files changed:**
  - `src/FortressAI.Web/Controllers/MemoryController.cs` — export endpoint added
  - `src/FortressAI.Web/Components/Pages/Memory.razor` — export button added
- **No DB migrations. No new env vars. No harness changes.**

---

## Resources

| Resource | Value |
|---|---|
| ECR Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:0c113528` |
| Image Digest | `sha256:76504d199415db16d70f0cd89608ca6a999d55b04de91954d65aa1ded237ac55` |
| Task Def | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:165` |
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `fred-dev` |
| Previous Task Def | `fred-dev:164` (rollback target) |

---

## Deployment Steps

1. ✅ Verified `0c113528` at HEAD
2. ✅ Pre-flight checks passed (docker-build + deploy)
3. ✅ Docker build — `docker build --no-cache -f fait/Dockerfile.debian -t fred-chat:0c113528 .`
4. ✅ ECR login + tag + push
5. ✅ Cloned `fred-dev:164`, updated image to `fred-chat:0c113528`, registered as `fred-dev:165`
6. ✅ `aws ecs update-service` → `fred-dev:165`
7. ✅ `aws ecs wait services-stable` — completed successfully
8. ✅ Verified 1/1 RUNNING with correct image digest

---

## Verification

```json
{
  "lastStatus": "RUNNING",
  "taskDef": "arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:165",
  "image": "742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:0c113528",
  "digest": "sha256:76504d199415db16d70f0cd89608ca6a999d55b04de91954d65aa1ded237ac55"
}
```

---

## Rollback

If rollback needed: `fred-dev:164` (`fred-chat:975c2d39`)

---

## Notes

- Epic 4 final WI. All Epic 4 work is now live in fred-dev.
- No issues encountered. Clean build, clean deploy.
