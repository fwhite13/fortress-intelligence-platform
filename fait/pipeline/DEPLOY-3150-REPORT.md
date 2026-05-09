# Deploy Report — ADO#3150: Wizard Fields Injected Into System Prompt

**Date:** 2026-05-09  
**Deployed by:** War Machine (Rhodey / devops subagent)  
**Session:** rhodey-deploy-3150

---

## Summary

Deployed commit `7a736d8b` which injects all setup wizard fields into the FAIT system prompt via `AssistantConfigService.GetPersonalitySystemPrompt`.

---

## What Was Deployed

- **Commit:** `7a736d8b` — `fix(fait#3150): inject wizard fields into system prompt — PreferredName, Role, Responsibilities, CommunicationStyle, ResponseFormat, ShowCitations, UseCasesJson, AdditionalContext`
- **Changed file:** `fait/Services/AssistantConfigService.cs` (+61 lines, -3 lines)
- **Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:7a736d8b`
- **Image digest:** `sha256:92baadcfc1fe53cf72159ea8e397c6a0fa2be6cc2eb286ed5bf15ba05b6aa0a0`
- **Task definition:** `fred-dev:142` (cloned from `fred-dev:141`)
- **Service:** `fred-dev` on cluster `fortress-tools-cluster`

---

## Deployment Steps Completed

| Step | Status |
|------|--------|
| Pre-flight check (credentials: fortress-tools-deployer) | ✅ PASS |
| `docker build --no-cache -f fait/Dockerfile -t fred-chat:7a736d8b .` | ✅ SUCCESS |
| `docker tag` → ECR URI | ✅ |
| ECR login | ✅ |
| `docker push ... fred-chat:7a736d8b` | ✅ SUCCESS |
| Task def registered as `fred-dev:142` | ✅ |
| `ecs update-service --force-new-deployment` | ✅ |
| `ecs wait services-stable` | ✅ STABLE |
| CloudWatch logs — clean startup, no errors | ✅ |
| ADO#3150 → Resolved | ✅ |

---

## Verification

**ECS Service State (post-deploy):**
```
status:  ACTIVE
running: 1
pending: 0
desired: 1
taskDef: arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:142
```

**CloudWatch Startup:**
- `Application started` ✅
- `Database initialization complete` ✅
- `DataProtectionKeys` already exists warning — non-fatal, expected ✅
- All MCP transports (devops, brave, m365) responding HTTP 200 ✅
- No error-level log entries ✅

**Task Def Preserved:**
- `Fargate__ContainerName = fait-v2-agent-harness` ✅
- `taskRoleArn = arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` ✅
- All env vars preserved exactly from `fred-dev:141` ✅

---

## Rollback

Previous task def: `fred-dev:141` (image `fred-chat:1bc3bb3f`)

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:141 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

---

## ADO

- **WI:** [ADO#3150](https://dev.azure.com/FortressAffinityGroup/74c75814-3f18-429a-be96-5c068deb0632/_workitems/edit/3150)
- **State:** Resolved
