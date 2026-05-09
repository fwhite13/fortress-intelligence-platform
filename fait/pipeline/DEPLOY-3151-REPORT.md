# Deploy Report — ADO#3151
**HarnessEvent JsonPropertyName Fix**
**Date:** 2026-05-09
**Deployed by:** War Machine (Rhodey, devops subagent)

---

## Summary

Deployed the fix for ADO#3151: `[JsonPropertyName]` attributes added to `HarnessEvent` positional record in `IUserAgentRuntime.cs`. This resolves camelCase deserialization failure for `inputTokens`/`outputTokens` from the harness SSE `done` event, so token counts now correctly appear below assistant messages in `/chat`.

---

## What Was Deployed

| Field | Value |
|---|---|
| Commit | `b3d571b7` |
| Image | `fred-chat:b3d571b7` |
| ECR URI | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:b3d571b7` |
| Image digest | `sha256:970a544fc6363ada8798b59a92329b3856ca149ff7affd54ca10e09703708ef6` |
| Task definition | `fred-dev:143` |
| Previous task def | `fred-dev:142` (image `fred-chat:7a736d8b`) |
| ECS cluster | `fortress-tools-cluster` |
| ECS service | `fred-dev` |

---

## Build

- Built from monorepo root: `cd /home/fredw/projects/fip && docker build --no-cache -f fait/Dockerfile -t fred-chat:b3d571b7 .`
- Used `Dockerfile` (standard, not `.debian` — FORMS-specific constraint doesn't apply here)
- Build completed successfully

---

## Task Definition

- Cloned `fred-dev:142`, updated image to `fred-chat:b3d571b7`
- All env vars preserved including:
  - `Fargate__ContainerName = fait-v2-agent-harness` ✅
  - All KB IDs, auth config, DB config preserved ✅
- `taskRoleArn`: `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` ✅
- Registered as `fred-dev:143`

---

## Deployment

- `aws ecs update-service --force-new-deployment` triggered at ~18:39 EDT
- Old task (142) drained, new task (143) became RUNNING at ~18:40 EDT
- Final state: RUNNING=1, PENDING=0, DESIRED=1 ✅

---

## Verification

**CloudWatch logs (`/ecs/fred-dev`, task `c8f626d9b61a4c309a72f56c30c1f04f`):**
- Database initialization complete ✅
- Application started, listening on `http://[::]:8080` ✅
- MCP tool registration: devops, brave, m365 all → 200 ✅
- No errors or exceptions in startup logs ✅

---

## ADO Update

- ADO#3151 → **Resolved**
- Comment posted with task def, digest, and fix description

---

## Rollback

To revert to previous:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:142 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```
