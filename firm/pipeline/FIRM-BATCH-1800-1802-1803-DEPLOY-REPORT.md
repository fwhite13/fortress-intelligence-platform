# FIRM Deploy Report — ADOs #1800 + #1802 + #1803

**Date:** 2026-04-13  
**Deployed by:** War Machine (Rhodey / devops)  
**Batch:** FIRM-BATCH-1800-1802-1803

---

## Summary

✅ **DEPLOY SUCCEEDED** — firm-web:85 is live and healthy.

---

## Deploy Timeline

| Time (EDT) | Event |
|---|---|
| 18:17 | Pre-deploy state captured — firm-web:84 |
| 18:17 | CodeBuild fip-firm-build triggered |
| 18:17 | ADO start comments posted (#1800, #1802, #1803) |
| 18:19 | Build SUCCEEDED |
| 18:19 | Task def firm-web:85 registered with :latest image |
| 18:19 | ECS service updated to firm-web:85 |
| 18:19 | Service HEALTHY — 1/1 running |
| 18:19 | ADO complete comments posted (#1800, #1802, #1803) |

---

## Build Details

- **Project:** fip-firm-build  
- **Build ID:** `fip-firm-build:f521f793-c9c4-4947-8db0-88dab91195bc`  
- **Status:** SUCCEEDED  
- **Duration:** ~90 seconds

---

## ECS Details

| Field | Value |
|---|---|
| Cluster | fortress-tools-cluster |
| Service | firm-web |
| Pre-deploy task def | arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:84 |
| New task def | arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:85 |
| Image | 742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:latest |
| Running / Desired | 1 / 1 |
| Health | ✅ HEALTHY |

---

## ADOs Covered

| ADO | Start Comment | Complete Comment |
|---|---|---|
| #1800 | ✅ Posted | ✅ Posted |
| #1802 | ✅ Posted | ✅ Posted |
| #1803 | ✅ Posted | ✅ Posted |

---

## Rollback

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service firm-web \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:84 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Notes

- Task def gap workaround applied as required: `:latest` image registered manually as firm-web:85
- Previous task def (firm-web:84) was pinned to commit SHA `6030c7ef1e0e78f5bbda5aaa9ad823410c316346`
- Service stabilized immediately — no deployment delay observed
