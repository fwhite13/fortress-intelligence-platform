# Deploy Report — ADO#3154

**Date:** 2026-05-09  
**Engineer:** Rhodey (DevOps subagent)  
**WI:** [ADO#3154 — BuildSystemPromptAsync S3 preference](https://dev.azure.com/FortressAffinityGroup/74c75814-3f18-429a-be96-5c068deb0632/_workitems/edit/3154)

---

## What Was Deployed

`BuildSystemPromptAsync` in `AssistantConfigService` — prefers S3 SOUL.md/USER.md over DB fields when building personality system prompts. Falls back gracefully to DB fields if S3 unavailable. Part of Epic 1.4 (user workspace provisioning).

---

## Build

| Field | Value |
|-------|-------|
| Commit | `ba30f846` |
| Image tag | `fred-chat:ba30f846` |
| ECR URI | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:ba30f846` |
| Image digest | `sha256:8fe908ea89b9284ec025aafd58d85754403acd2e6ada7676aa4b8f32166e83f5` |
| Build method | `docker build --no-cache -f fait/Dockerfile` from monorepo root |
| Build result | ✅ Success (warnings only, 0 errors) |

---

## ECS Deployment

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `fred-dev` |
| Previous task def | `fred-dev:146` (image `fred-chat:61b4ec75`) |
| New task def | `fred-dev:147` |
| Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/3da0a0459a6e46cabbc19f0ba05584a7` |
| Deploy status | ✅ STABLE |
| Health | ✅ HEALTHY |
| Running count | 1 |

---

## Verification

- ✅ `Fargate__ContainerName = fait-v2-agent-harness` preserved in task def
- ✅ `taskRoleArn = arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` preserved
- ✅ All 43 env vars preserved
- ✅ CloudWatch logs: clean startup, DB initialization complete, `Application started`, no errors
- ✅ MCP tools registered: devops, brave, m365

---

## ADO Update

- State: **Resolved**
- Comment posted with task def revision and image digest

---

## Depends On

- ADO#3153 (1.4-A) — UserProvisioningService with S3 workspace seeding (deployed previously)
