# Deploy Report — ADO#3138
**Pre-signed Avatar URL in Chat Header and Message Bubbles**

**Date:** 2026-05-09  
**Deployer:** Rhodey (DevOps subagent)  
**Status:** ✅ SUCCESS

---

## What Deployed

- **Commit:** `8b5fdc71` — feat(fait#3138): pre-signed avatar URL in chat header and MessageBubble
- **Docker Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:8b5fdc71`
- **Image Digest:** `sha256:dbb162c4eb5a7ea67a54daf341dd62b62c46266547ee30f4f7556934963da9b6`
- **Task Definition:** `fred-dev:154` (cloned from `fred-dev:153`)
- **Service:** `fred-dev` on cluster `fortress-tools-cluster`

---

## Verification

| Check | Result |
|-------|--------|
| Commit at HEAD | ✅ `8b5fdc71` confirmed |
| Docker build (Dockerfile.debian, --no-cache) | ✅ Success |
| ECR push | ✅ Pushed |
| Task def `Fargate__ContainerName` | ✅ `fait-v2-agent-harness` |
| Task def `taskRoleArn` | ✅ `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` |
| Task def image | ✅ `fred-chat:8b5fdc71` |
| ECS service stable | ✅ STABLE |
| Running count | ✅ 1 |
| Pending count | ✅ 0 |
| Deployments | ✅ Single PRIMARY |

---

## ADO Update

- **ADO#3138** → State: **Resolved**
- Comment: `Deployed fred-chat:8b5fdc71, fred-dev:154. Pre-signed avatar URL wired into chat header and MessageBubble. Service HEALTHY.`

---

## Rollback

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:153 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

_Deployed by Rhodey — War Machine DevOps_
