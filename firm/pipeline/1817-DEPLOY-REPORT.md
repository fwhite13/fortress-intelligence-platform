# Deploy Report — ADO #1817 — FIRM Retranscribe Button

**Date:** 2026-04-14 00:00–00:06 EDT  
**Agent:** War Machine (devops subagent)  
**ADO:** #1817  
**Feature:** Retranscribe button  
**Commit:** `b36c30a`  
**Service:** `firm-web` on `fortress-tools-cluster`

---

## Summary

| Step | Status |
|------|--------|
| CodeBuild `fip-firm-build` build #57 | ✅ SUCCEEDED |
| Task def `firm-web:88` registered | ✅ |
| ECS service updated to `firm-web:88` | ✅ |
| ECS health 1/1 | ✅ HEALTHY |

---

## Build Details

- **Build ID:** `fip-firm-build:099d9a18-34a0-4ee7-aaec-18339570c497`
- **Build #:** 57
- **Duration:** ~4.5 minutes
- **ECR Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:latest`

---

## Task Definition

- **Previous:** `firm-web:87`
- **New:** `firm-web:88`
- **Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:latest`
- **Env vars preserved:** 42 (including `Firm__VpBotUrl` ✅)

---

## ECS Health

```
running: 1
desired: 1
pending: 0
taskDef: arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:88
```

---

## Rollback

```bash
aws ecs update-service --cluster fortress-tools-cluster --service firm-web \
  --task-definition firm-web:87 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## ADO Comments

- **Start:** Comment #743877 posted at 2026-04-14T04:00:36Z
- **Complete:** Posted after ECS healthy
