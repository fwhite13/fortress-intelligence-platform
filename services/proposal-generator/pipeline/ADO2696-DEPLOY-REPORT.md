# ADO#2696 Deploy Report

**WI:** ADO#2696 — fix set_cell_width/set_table_width remove-before-append  
**Deployed by:** War Machine (Rhodey)  
**Date:** 2026-05-01  
**Commit:** `01a5860`

---

## Summary

Fix for `set_cell_width` / `set_table_width` to remove existing XML before append, preventing duplicate XML nodes accumulating on repeated calls.

---

## Image

| Tag | Digest |
|-----|--------|
| `fip-proposal-generator:01a5860` | `sha256:1b99891662eb27a9887dbc8cae43f45c7ade5be79e5047024494d5b69551d6bd` |
| `fip-proposal-generator:latest` | same |

ECR repo: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator`

---

## ECS

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `proposal-generator-dev` |
| Previous task def | `proposal-generator-dev:28` |
| New task def | `proposal-generator-dev:29` |
| Status | RUNNING 1/1 |
| `/health` | 200 |

---

## Rollback

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition proposal-generator-dev:28 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Build Notes

- Docker build: `--no-cache` ✅
- LibreOffice 25.8.1 installed in image ✅
- npm ci --only=production ✅ (183 packages)
- Build time: ~2 min 30 sec
