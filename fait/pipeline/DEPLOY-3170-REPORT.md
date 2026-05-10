# Deploy Report — ADO#3170: ScheduledTaskBackgroundService

**Date:** 2026-05-10  
**Deployed by:** War Machine (Rhodey) — DevOps Subagent  
**Commit:** `d8505e00`  
**WI:** ADO#3170 — 3.2-A ScheduledTaskBackgroundService + distributed lock + retry

---

## What Was Deployed

ScheduledTaskBackgroundService — a singleton IHostedService that:
- Polls every 60s for due scheduled tasks
- Claims tasks via MySQL distributed lock (atomic `ExecuteSqlRawAsync`)
- Dispatches to user's Fargate session via `SendTurnAsync`
- Writes `scheduled_task_runs` run history
- Handles retry logic (retry once at +5min, fail permanently at failure_count ≥ 2)
- Recovers stale locks (>30 min) from crashed instances

---

## Deployment Summary

| Item | Value |
|------|-------|
| Image | `fred-chat:d8505e00` |
| ECR Digest | `sha256:b30ae48f4955decf7e72a896e47c5fbdc384633afbf5af2956e8b7af69651420` |
| Task Definition | `fred-dev:157` |
| Previous Task Def | `fred-dev:156` |
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `fred-dev` |
| DB Migration | None required |

---

## Verification

- ✅ Docker build: success (Dockerfile.debian, no-cache)
- ✅ ECR push: `fred-chat:d8505e00` pushed
- ✅ Task def `fred-dev:157` registered
  - `Fargate__ContainerName = fait-v2-agent-harness` ✅
  - `taskRoleArn` present ✅
- ✅ ECS service updated to `fred-dev:157`
- ✅ Service STABLE: 1 running, 0 pending, PRIMARY deployment
- ✅ CloudWatch log confirmed: `"ScheduledTaskBackgroundService starting, poll interval: 60s"`
- ✅ ADO#3170 → Resolved

---

## Rollback

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:156 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

_Deploy completed: 2026-05-10 ~00:57 EDT_
