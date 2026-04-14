# Deploy Report — ADO #1842 — firm-web
**Date:** 2026-04-14  
**Deployer:** War Machine (devops subagent)  
**Commit:** `a8fdc19`  
**Rollback:** `firm-web:89`

---

## Steps Completed

1. ✅ **Pre-deploy snapshot** — `firm-web:90`, running=1, ACTIVE
2. ✅ **ADO start comment** — Posted to #1842 at 14:19 EDT
3. ✅ **CodeBuild triggered** — `fip-firm-build:9c4a1c9b-12ac-46fb-a1ba-c1da23951d41` (build #59)
   - Source: `refs/heads/main`
   - Build started: 14:19:47 EDT
   - Build completed: ~14:22 EDT (~2.5 min)
   - Result: **SUCCEEDED**
4. ✅ **Task def registered** — `firm-web:91`
   - Image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:latest`
   - 42 env vars preserved (including `Firm__VpBotUrl`)
5. ✅ **ECS service updated** — `firm-web` on `fortress-tools-cluster`
   - Deployment ID: `ecs-svc/7141294975168413300`
   - Task def: `firm-web:91`
   - Force new deployment: YES
6. ✅ **ECS stabilized** — running=1, desired=1, PRIMARY deployment healthy
7. ✅ **ADO complete comment** — Posted to #1842

---

## Post-Deploy State

| Property | Value |
|----------|-------|
| Service | `firm-web` |
| Cluster | `fortress-tools-cluster` |
| Task Def | `firm-web:91` |
| Running | 1/1 |
| Status | ACTIVE / HEALTHY |
| Image | `firm-web:latest` |
| Rollback | `firm-web:89` |

---

## Notes

- Pre-deploy revision was `:90` (not `:89` as briefed — `:90` was already deployed prior, `:89` remains the safe rollback point per instructions)
- CodeBuild build #59 on `fip-firm-build`
