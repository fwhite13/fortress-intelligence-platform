# NEXUS Deploy Report — ADO #1820
**Discovery truncation limits**
**Date:** 2026-04-14
**Deployed by:** War Machine (devops subagent)

---

## Summary

| Field | Value |
|---|---|
| ADO Work Item | #1820 |
| Service | `nexus-web` on `fortress-tools-cluster` |
| CodeBuild Project | `fip-nexus-build` |
| Build # | 35 |
| Build ID | `fip-nexus-build:73faea86-ef5a-490d-b0a3-aa96723131de` |
| Build Status | **SUCCEEDED** |
| New Task Def | `nexus-web:33` |
| Rollback Target | `nexus-web:32` |
| ECS Health | **1/1 RUNNING** |

---

## Steps Completed

1. ✅ **ADO start comment posted** — #1820 at 00:25 EDT
2. ✅ **CodeBuild triggered** — `fip-nexus-build` build #35
3. ✅ **CodeBuild SUCCEEDED** — ~1m 30s, source branch: `main`
4. ✅ **Task def registered** — `nexus-web:33` (image: `nexus-web:latest`)
5. ✅ **ECS service updated** — `nexus-web` → `nexus-web:33`, force-new-deployment
6. ✅ **ECS service stabilized** — running=1, desired=1, pending=0
7. ✅ **ADO complete comment posted** — #1820 at 00:27 EDT

---

## ECS Final State

```json
{
  "running": 1,
  "desired": 1,
  "pending": 0,
  "taskDef": "arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:33"
}
```

---

## Rollback Procedure

```bash
aws ecs update-service --cluster fortress-tools-cluster --service nexus-web \
  --task-definition nexus-web:32 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Timeline

| Time (EDT) | Event |
|---|---|
| 00:25:25 | CodeBuild triggered (build #35) |
| 00:25:33 | PROVISIONING |
| 00:25:54 | PRE_BUILD |
| 00:26:15 | BUILD |
| 00:26:58 | SUCCEEDED |
| 00:27:07 | Task def `nexus-web:33` registered |
| 00:27:15 | ECS service updated to `:33` |
| 00:27:43 | ECS STABLE — 1/1 running |

**Total deploy time: ~2m 20s**
