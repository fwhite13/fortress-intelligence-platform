# Deploy Report — ADO#3155
**Resumption Brief Fixes**
**Date:** 2026-05-09
**Deployer:** Rhodey (War Machine)

---

## Summary

Two-image deploy for ADO#3155 — fixes for the resumption brief feature:
- **Harness:** Skip brief entirely when no history/MEMORY.md (no generic fallback text)
- **Fred-chat:** Brief card moved to bottom of message list; `__brief_start__` sentinel removed

---

## Images Deployed

| Image | Tag | ECR Digest |
|-------|-----|-----------|
| `fait-v2-agent-harness` | `d66ababa` | `sha256:59de908c1c05c2d3734f21c978dd08f7c32faaf3d38681c88ebd9dcd43c03680` |
| `fred-chat` | `09a2e08b` | `sha256:c8e87ce442b30a48dc4b9e4582c4b4a80b747db96e90897d1e57b6e1224a998b` |

---

## Task Definitions

| Family | Revision | Change |
|--------|----------|--------|
| `fait-v2-agent-harness` | `:11` | Image updated to `d66ababa` |
| `fred-dev` | `:149` | Image → `09a2e08b`, `Fargate__TaskDefinition` → `fait-v2-agent-harness:11` |

---

## ECS Deployment

- **Cluster:** `fortress-tools-cluster`
- **Service:** `fred-dev`
- **Task Definition:** `fred-dev:149`
- **Prior:** `fred-dev:148` (fred-chat:`6de82e9e`, harness:`:10`)
- **Result:** RUNNING=1, PENDING=0, stable single deployment ✅

### Preserved Environment Variables
All env vars from `fred-dev:148` preserved:
- `Fargate__HarnessPort = 3000`
- `Fargate__SubnetIds = subnet-08e1d4f1b5530f39e,subnet-051bfcf5b07661809`
- `Fargate__TaskDefinition = fait-v2-agent-harness:11` ← updated
- `Fargate__SecurityGroupIds = sg-0fb53615b1eb4a175`
- `Fargate__ContainerName = fait-v2-agent-harness`
- `Fargate__ClusterArn = arn:aws:ecs:us-east-1:742932328420:cluster/fortress-tools-cluster`

**taskRoleArn:** `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` ✅ preserved

---

## CloudWatch Verification

Log stream: `ecs/fred/9fc5d348ff9946f383292013ba20836f`

Startup clean:
- ✅ Database initialization complete
- ✅ Now listening on: http://[::]:8080
- ✅ Application started
- ✅ MCP transports healthy (devops, brave, m365)
- ✅ No errors

---

## ADO

- **ADO#3155** → **Resolved**
- Comment: "Deployed harness:d66ababa (fait-v2-agent-harness:11) + fred-chat:09a2e08b (fred-dev:149). Brief skips when no real context. Brief renders at bottom of chat."

---

## Rollback

If needed:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:148 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```
