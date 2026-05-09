# Deploy Report — ADO#3151 Fix

**Date:** 2026-05-09  
**Deployed by:** Rhodey (War Machine — DevOps subagent)  
**ADO Work Item:** [#3151](https://dev.azure.com/FortressAffinityGroup/74c75814-3f18-429a-be96-5c068deb0632/_workitems/edit/3151)

---

## Summary

Deployed harness fix for ADO#3151 — metadata event ordering. The harness was emitting the `done` SSE event with `inputTokens=0` / `outputTokens=0` because the `metadata` event from Bedrock's ConverseStream was being processed _after_ `messageStop`. This fix captures the metadata event in the correct position.

---

## What Was Deployed

| Component | Before | After |
|-----------|--------|-------|
| Harness image | `fait-v2-agent-harness:73f99147` | `fait-v2-agent-harness:7e8798c1` |
| Harness task def | `fait-v2-agent-harness:9` | `fait-v2-agent-harness:10` |
| fred-dev task def | `fred-dev:144` | `fred-dev:145` |

**ECR image digest:** `sha256:395820f0c0b08cafb4e2cc41635679fe7a7ffc69fb7566e7cde98bf378e3cf28`

---

## Commit

```
7e8798c1 fix(fait#3151): capture metadata event after messageStop
         — inputTokens/outputTokens now correctly populated from Bedrock ConverseStream
```

**File changed:** `fait-v2/agent-harness/harness-server.js` (+6/-1)

---

## Deployment Steps

1. ✅ Docker build — `fait-v2-agent-harness:7e8798c1` (with `--no-cache`)
2. ✅ ECR push — `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:7e8798c1`
3. ✅ Registered `fait-v2-agent-harness:10` — image updated, all other config preserved
4. ✅ Registered `fred-dev:145` — `Fargate__TaskDefinition` updated from `:9` → `:10`
5. ✅ `aws ecs update-service --force-new-deployment` triggered
6. ✅ Service stabilized: `RUNNING=1, PENDING=0` within ~70 seconds

---

## Verification

### ECS Service
- **Task:** `b0430db2676946b9a9c3588641d699c7` (`fred-dev:145`)
- **Status:** `RUNNING`
- **Container:** `fred` image `fred-chat:b3d571b7` — healthy

### Env Var Confirmation (fred-dev:145)
```
Fargate__TaskDefinition   = fait-v2-agent-harness:10  ✅
Fargate__ContainerName    = fait-v2-agent-harness      ✅
Fargate__ClusterArn       = arn:aws:ecs:us-east-1:742932328420:cluster/fortress-tools-cluster
Fargate__SubnetIds        = subnet-08e1d4f1b5530f39e,subnet-051bfcf5b07661809
Fargate__SecurityGroupIds = sg-0fb53615b1eb4a175
Fargate__HarnessPort      = 3000
```

### CloudWatch Logs
- `/ecs/fred-dev` — Clean startup, no errors. ASP.NET Core listening on `:8080`, MCP tools loaded (devops, brave).
- No exceptions or crash loops observed.

---

## ADO Update

ADO#3151 → **Resolved**

Comment posted:
> Deployed harness:7e8798c1 (fait-v2-agent-harness:10), fred-dev:145. metadata event now captured after messageStop — inputTokens/outputTokens correctly populated from Bedrock ConverseStream. Token counts should appear in UI.

---

## Cost Impact

No new resources. Task definition revision only — no cost change.

---

## Rollback

If needed:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:144 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

_Shipped clean. 🦾_
