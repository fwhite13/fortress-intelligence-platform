# ADO#2704 Deploy Report — proposal-generator-dev

**Date:** 2026-05-04  
**Deployed by:** War Machine (Rhodey)  
**Commit:** `97653a1`  
**Description:** header pgMar fix + full cell vAlign/spacing fix  

---

## Summary

| Field | Value |
|-------|-------|
| Service | `proposal-generator-dev` |
| Cluster | `fortress-tools-cluster` |
| ECR Repo | `fip-proposal-generator` |
| Image Tag | `97653a1` |
| Image Digest | `sha256:7efb7b28de5a089cfa6a8bba9c91733d0e0ab2ee5a19a6294ac7633420d6c464` |
| New Task Def | `proposal-generator-dev:31` |
| Previous Task Def | `proposal-generator-dev:30` (rollback target) |
| Health Check | `200 OK` |
| ECS Status | RUNNING 1/1 |

---

## Steps Completed

1. ✅ **Pre-deploy snapshot** — Task def `proposal-generator-dev:30` confirmed ACTIVE
2. ✅ **ADO pre-flight comment** — Posted to ADO#2704 (comment id 772283)
3. ✅ **ECR login** — Authenticated with `fortress-tools-deployer` credentials
4. ✅ **Docker build** — `--no-cache` build from monorepo root, `services/proposal-generator/Dockerfile`
5. ✅ **ECR push** — Pushed `:97653a1` and `:latest` tags
6. ✅ **Task def registered** — `proposal-generator-dev:31` pinned to commit SHA
7. ✅ **ECS service updated** — `--force-new-deployment` triggered
8. ✅ **ECS stabilized** — RUNNING 1/1, pending 0
9. ✅ **Health check** — `/health` → `200 OK`
10. ✅ **ADO completion comment** — Posted to ADO#2704 (comment id 772291)

---

## Rollback

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition proposal-generator-dev:30 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Notes

- WSL2 Docker credential helper (`docker-credential-desktop.exe`) bypassed for ECR login; `~/.docker/config.json` restored to original state post-push.
- Build was clean with no cache; all layers pushed successfully.
