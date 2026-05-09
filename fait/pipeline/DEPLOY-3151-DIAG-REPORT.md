# Deploy Report: ADO#3151 — Diagnostic Harness Build

**Date:** 2026-05-09  
**Deployer:** Rhodey (devops subagent)  
**ADO:** [#3151 — Token counts not shown on assistant responses](https://dev.azure.com/FortressAffinityGroup/74c75814-3f18-429a-be96-5c068deb0632/_workitems/edit/3151)

---

## Deployment Type
ECS task definition update (harness image + fred-dev env var)

---

## Pre-Deploy Snapshot
- Previous harness task def: `fait-v2-agent-harness:8`
- Previous harness image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:2ce64b11`
- Previous fred-dev task def: `fred-dev:143`
- Service state: RUNNING=1, PENDING=0

---

## Steps Completed

1. ✅ Pre-deploy snapshot captured — harness:8, fred-dev:143
2. ✅ Commit verified — `73f99147` (diag: log inputTokens/outputTokens before done event)
3. ✅ Docker build — `fait-v2-agent-harness:73f99147` — success  
   - Image digest: `sha256:170171330828d39b7a94a28fba8e502823144b3ad985ebc495e1554266f8dd3c`
4. ✅ ECR push — `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:73f99147`
5. ✅ Task def registered — `fait-v2-agent-harness:9`  
   - ARN: `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:9`
6. ✅ `fred-dev:144` registered — `Fargate__TaskDefinition` updated to `fait-v2-agent-harness:9`
   - ARN: `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:144`
7. ✅ `fred-dev` service updated — `--force-new-deployment`
8. ✅ Service stable — RUNNING=1, PENDING=0
9. ✅ CloudWatch logs verified — clean startup (DataProtectionKeys already exists = non-fatal, expected)
10. ✅ Env vars verified in fred-dev:144:
    - `Fargate__TaskDefinition = fait-v2-agent-harness:9` ✅
    - `Fargate__ContainerName = fait-v2-agent-harness` ✅
11. ✅ ADO#3151 updated — state=Active, comment added

---

## Deployment Artifacts

| Artifact | Value |
|----------|-------|
| Commit | `73f99147` |
| ECR Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:73f99147` |
| ECR Digest | `sha256:170171330828d39b7a94a28fba8e502823144b3ad985ebc495e1554266f8dd3c` |
| Harness Task Def | `fait-v2-agent-harness:9` |
| Fred-Dev Task Def | `fred-dev:144` |
| Running Task | `cab87fbe3e474a5182a5357cbc5ef192` |

---

## What to Check Next

After the next FAIT chat response in fred-dev, check CloudWatch `/ecs/fred-dev` log stream for:

```
[harness] /turn: done event — inputTokens=X, outputTokens=Y
```

This confirms the diagnostic log line from commit `73f99147` is firing. If X and Y are non-zero, Bedrock is returning usage metadata and the issue is purely in deserialization (confirming the fix in ADO#3151 is correct). If they're both 0, Bedrock isn't returning usage metadata at all.

---

## Rollback Plan

### Pre-Deploy State
- Harness task def: `fait-v2-agent-harness:8`
- Fred-dev task def: `fred-dev:143`

### Rollback Commands
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:143 \
  --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --profile fortress-tools-deployer --region us-east-1
```

### Rollback SLA
< 5 minutes (ECS)

---

## Deployment Time
- Build start: ~19:00 EDT
- Service stable: ~19:04 EDT
- Total duration: ~4 minutes
