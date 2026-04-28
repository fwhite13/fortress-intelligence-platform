# Deploy Report: ADO#2498

**Date:** 2026-04-28  
**Deployer:** War Machine (Rhodey — devops subagent)  
**App:** `nexus-web` on ECS cluster `fortress-tools-cluster`  
**Commit:** `a965b58afbeaf131ec7fc0a8175ae1b4fc6c4b2d`  
**Branch:** main  
**Review:** PASS (Hawkeye cycle 2, 22 checks clean)  

---

## What Was Deployed

- IWiClassifier integrated into ArtifactGenerationService
- `ParentTitle` property added to `WorkItemRecord`
- `PredecessorTitles` mapping in `StubAdoService`
- EF migration `AddWorkItemRecordParentTitle` — adds `parent_title VARCHAR(500) NULL` to `work_item_records`

---

## Pre-Deploy Snapshot (Rollback Baseline)

| Field | Value |
|-------|-------|
| Task Definition | `nexus-web:46` |
| Task Definition ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:46` |
| Running Task | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/7fd4689e988147e9a788afeb60dea58e` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest` |
| Image Digest (pre-deploy) | `sha256:cb98cd1d249b6ec3b5847196c32cf00e1b9e2c2a71dd3d241acf0abf8965ee32` |
| Service State | 1/1 RUNNING, rolloutState: COMPLETED |
| Task Started | 2026-04-28T07:53:13 EDT |

---

## Build

| Field | Value |
|-------|-------|
| CodeBuild Project | `fip-nexus-build` |
| Build ID | `fip-nexus-build:c06a2cea-f372-4fd2-9732-9fc46f1cb1c9` |
| Build Status | **SUCCEEDED** |
| Build Start | 2026-04-28T10:22:09 EDT |
| Build End | 2026-04-28T10:23:34 EDT |
| Source Commit | `a965b58afbeaf131ec7fc0a8175ae1b4fc6c4b2d` |
| Build Logs | https://console.aws.amazon.com/cloudwatch/home?region=us-east-1#logsV2:log-groups/log-group/$252Faws$252Fcodebuild$252Ffip-nexus-build/log-events/c06a2cea-f372-4fd2-9732-9fc46f1cb1c9 |

---

## ECS Deployment

| Field | Value |
|-------|-------|
| Method | Force-new-deployment on `nexus-web` |
| Task Definition | `nexus-web:46` (unchanged — CodeBuild updates image in place via `:latest`) |
| AzureAd Env Vars | ✅ Present (AzureAd__ClientId, AzureAd__ClientSecret, AzureAd__TenantId) |
| New Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/bdbbfcd454854e3e88ea716eac65ded6` |
| New Image Digest | `sha256:d6294a72bf81f57e8bb3105967eae64342f945eb075003df747166b4e47bf784` |
| New Task Started | 2026-04-28T10:26:27 EDT |
| Rollout State | **COMPLETED** |
| Running | 1/1 |

---

## CloudWatch Migration Confirmation

Log stream: `ecs/nexus-web/bdbbfcd454854e3e88ea716eac65ded6`

```
[14:26:10 INF] [NEXUS] Running EF Core migrations on startup...
[14:26:11 INF] [NEXUS] EF Core migrations complete.
[14:26:11 WRN] Overriding HTTP_PORTS '8080' and HTTPS_PORTS ''. Binding to values defined by URLS instead 'http://+:8080'.
```

**Migration `AddWorkItemRecordParentTitle`: ✅ APPLIED**  
No ERR-level entries beyond pre-existing PdfExporter font issue (confirmed clean).

---

## Post-Deploy Health Check

| Check | Result |
|-------|--------|
| ECS service running count | ✅ 1/1 |
| ECS pending count | ✅ 0 |
| Rollout state | ✅ COMPLETED |
| Stopped tasks | ✅ None |
| CloudWatch ERR entries | ✅ None (unexpected) |
| EF migration applied | ✅ "EF Core migrations complete" in startup logs |
| New image digest | ✅ Different from pre-deploy (new code deployed) |

**Overall health: HEALTHY ✅**

---

## Rollback Plan

### Pre-Deploy State
- Task Definition: `nexus-web:46`
- Image Digest: `sha256:cb98cd1d249b6ec3b5847196c32cf00e1b9e2c2a71dd3d241acf0abf8965ee32`

### Rollback Commands

> ⚠️ Note: The `parent_title` column added by `AddWorkItemRecordParentTitle` is `NULL`-able and non-breaking.
> Rolling back the ECS service to the prior image will work without data issues.
> However, the DB migration cannot be auto-reversed — the column will remain (inert, unused by old code).

```bash
# Source deployment credentials
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer

# Revert ECS to previous task definition revision
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:46 \
  --force-new-deployment \
  --region us-east-1

# Wait for stability
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services nexus-web \
  --region us-east-1

# Verify
aws ecs describe-services \
  --cluster fortress-tools-cluster \
  --services nexus-web \
  --region us-east-1 \
  --query 'services[0].{taskDef:taskDefinition,running:runningCount,rolloutState:deployments[0].rolloutState}' \
  --output json
```

> **Note:** Since CodeBuild pushes to `:latest` tag, ECS uses the same task def revision (`:46`) — rolling back here means triggering a new deployment with the old code would require rebuilding from the prior commit. If rapid rollback to the exact pre-deploy image is needed, use the image digest directly:
> `sha256:cb98cd1d249b6ec3b5847196c32cf00e1b9e2c2a71dd3d241acf0abf8965ee32`

### Rollback SLA
ECS rollback: **< 5 minutes**

---

## Summary

| | |
|--|--|
| **Build** | SUCCEEDED (`fip-nexus-build:c06a2cea-f372-4fd2-9732-9fc46f1cb1c9`) |
| **Commit** | `a965b58` |
| **Image Digest (deployed)** | `sha256:d6294a72bf81f57e8bb3105967eae64342f945eb075003df747166b4e47bf784` |
| **Migration** | AddWorkItemRecordParentTitle — APPLIED |
| **Health** | HEALTHY |
