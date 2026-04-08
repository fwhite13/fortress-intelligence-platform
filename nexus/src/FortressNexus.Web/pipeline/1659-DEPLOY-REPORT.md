# WI #1659 — Deploy Report
**Date:** 2026-04-08  
**Deployer:** War Machine (devops subagent)  
**App:** nexus-web (ECS on fortress-tools-cluster)

---

## What Was Deployed

- **WI #1659:** `SpecGenerationService.GenerateAsync` — versioning fixed (MAX+1 instead of hardcoded 1)
- **WI #1659:** `NewSpecWizard.razor` — two-pass regen HandleSubmit, `_regenPending` flag, TODO comments
- **Target commit:** `21058b8` (fix #1659 — restore TODO comment at GenerateAsync Pass 2 call)
- **Image commit (built):** `f2924ec94c26e78704804b642ed1f158be81d67a` (origin/main HEAD at build time — includes #1659 + #1655 soft-delete)

> Note: `b5d0a14` (WI #1661) was 1 commit ahead of origin/main locally (unpushed) at deploy time, so build resolved to `f2924ec`. All #1659 changes confirmed included via `git merge-base --is-ancestor 21058b8 f2924ec`.

---

## Deployment Timeline

| Step | Time (EDT) | Detail |
|------|-----------|--------|
| WI #1660 steady-state wait | 15:02 → 15:09 | nexus-web:21 stabilized before starting |
| Snapshot nexus-web:21 | 15:09 | Rollback baseline captured |
| CodeBuild start | 15:10:48 | Build #20: `fip-nexus-build:bbf0e987-c28c-417f-a1c9-8a261e604e5a` |
| CodeBuild SUCCEEDED | 15:12:09 | ~1.5 min build |
| Task def registered | ~15:12 | `nexus-web:22` |
| ECS force-new-deployment | ~15:13 | `nexus-web:22` → PRIMARY |
| ECS steady state | ~15:20 | `nexus-web:22` COMPLETED, 1/1 running |
| Health check | ~15:20 | HTTP 403 (Cognito auth wall — expected) |

---

## Resources

| Resource | Value |
|----------|-------|
| Build ID | `fip-nexus-build:bbf0e987-c28c-417f-a1c9-8a261e604e5a` |
| Build # | 20 |
| ECR Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:f2924ec94c26e78704804b642ed1f158be81d67a` |
| Image Digest | `sha256:1cb6fc74bc80d66e6a3f738b66de3c92ebb7ed6a9d8c532c68be322cbd399d0a` |
| Task Def (live) | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:22` |
| Task Def (rollback) | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:21` |
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `nexus-web` |
| Health Check | `https://nexus.fortressam.ai/` → HTTP 403 ✅ |

---

## CloudWatch

- **Log group:** `/ecs/nexus-web`
- **Latest stream:** `ecs/nexus-web/d516319209ee4933914403ec63e607d0`
- **Startup:** EF Core migrations complete, no exceptions, no errors
- **Only warning:** `Overriding HTTP_PORTS` (normal — configured via URLS env var)

---

## Rollback

If rollback needed:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:21 \
  --force-new-deployment \
  --profile fortress-tools-deployer
```

---

## Result

✅ **DEPLOY SUCCESSFUL** — nexus-web:22 live, healthy, no exceptions
