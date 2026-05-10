# Deploy Report — ADO#3155 + ADO#3163

**Date:** 2026-05-10  
**Deployed by:** War Machine (Rhodey)  
**Service:** `fred-dev` on cluster `fortress-tools-cluster`  
**Result:** ✅ SUCCESS

---

## What Was Deployed

**Commit:** `3f6667be`  
> fix(fait#3155,#3163): verify resumption brief fix; task mode toggle pill with Task label and proper sizing

**Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:3f6667be`  
**Image digest:** `sha256:c78fa2a3d4bb781acffdc9770bd9c373b7f8a4879bcd456d0ade41ede5079906`  
**Task definition:** `fred-dev:153` (cloned from `fred-dev:152`, image updated)  
**Dockerfile used:** `fait/Dockerfile.debian` (MCR blocked on WSL2)

---

## Work Items Resolved

| ID | Title | State |
|----|-------|-------|
| ADO#3155 | 3146 follow-up: resumption brief shows generic text + renders at top of chat | Resolved |
| ADO#3163 | Task mode toggle pill has no label and is too small — styling broken | Resolved |

---

## Task Definition Verification

| Check | Result |
|-------|--------|
| `Fargate__ContainerName` | `fait-v2-agent-harness` ✅ |
| `taskRoleArn` | `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` ✅ |
| Image tag | `fred-chat:3f6667be` ✅ |

---

## ECS Service Status (post-deploy)

```json
{
  "running": 1,
  "pending": 0,
  "status": "ACTIVE",
  "deployments": [
    {
      "status": "PRIMARY",
      "running": 1,
      "desired": 1,
      "taskDef": "arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:153"
    }
  ]
}
```

---

## Rollback

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:152 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

_RHODEY DEPLOY: SUCCESS — fred-dev:153 — ADO#3155 + ADO#3163 deployed and HEALTHY_
