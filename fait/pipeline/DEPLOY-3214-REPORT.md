# Deploy Report — ADO#3214

**Date:** 2026-05-10  
**Agent:** War Machine (James Rhodes) — DevOps  
**Deployed by:** rhodey-deploy-3214 subagent  

---

## Summary

Deployed `fred-chat:3b7415a3` — ProtectedSessionStorage resumption brief guard fix — to `fred-dev`.

---

## What Was Deployed

- **WI:** ADO#3214 — `fix(fait#3214): prevent resumption brief re-firing on /chat nav — ProtectedSessionStorage guard`
- **Commit:** `3b7415a3`
- **Image tag:** `fred-chat:3b7415a3`
- **ECR URI:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:3b7415a3`
- **Image digest:** `sha256:52680c52fadd4651ea9d9a8312a2d52163c8d6b320263f6f7c897b5a64a439a5`

---

## Deployment Details

| Field | Value |
|-------|-------|
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `fred-dev` |
| Previous Task Def | `fred-dev:172` |
| New Task Def | `fred-dev:173` |
| Dockerfile | `fait/Dockerfile.debian` |
| Build flags | `--no-cache` |
| Region | `us-east-1` |
| AWS User | `fortress-tools-deployer` |

---

## Verification

- ✅ Commit `3b7415a3` confirmed at HEAD-1 before build
- ✅ Docker build: succeeded (no errors, warnings only)
- ✅ ECR push: `fred-chat:3b7415a3` and `red-chat:latest` pushed
- ✅ Task definition `fred-dev:173` registered
- ✅ ECS service updated to `fred-dev:173`
- ✅ Service stabilized: RUNNING / HEALTHY
- ✅ Running image digest matches pushed digest

---

## Rollback

If needed:
```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev --task-definition fred-dev:172 --region us-east-1
```

---

## Notes

- Blazor-only change: no DB migrations, no harness changes
- `fip-deploy.sh` wrapper was not used (ECR repo name mismatch: script references `fortress-ai-chat`, actual repo is `fred-chat`)
- Pre-flight docker-build.sh passed; deploy.sh flagged ECR name mismatch (expected, bypassed manually)
