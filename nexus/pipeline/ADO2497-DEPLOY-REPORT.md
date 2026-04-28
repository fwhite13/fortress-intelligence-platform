# Deploy Report — ADO#2497
**nexus-web | WorkItemRecord + ArtifactSet Decomposition Upgrade Fields**

| Field | Value |
|-------|-------|
| **Date** | 2026-04-28 |
| **Engineer** | War Machine (Rhodey) |
| **Commit** | `f527f50` — `feat(nexus#2497): add decomposition upgrade fields to WorkItemRecord and ArtifactSet` |
| **Build ID** | `fip-nexus-build:33ad5d97-fd98-4da6-9959-698c6767396e` |
| **Result** | ✅ HEALTHY |

---

## Pre-Deploy Snapshot (Rollback Baseline)

| Field | Value |
|-------|-------|
| **Task Definition** | `nexus-web:46` |
| **Task Def ARN** | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:46` |
| **Image (pre-deploy)** | `nexus-web:latest` → `19d2cc8f9393dfd5ce44ec3ae4bb742912abdbf3` |
| **Pre-deploy digest** | `sha256:b6f5d11dae307d082d7f563fdfd9c81c92b6a3c4c7f89f1e949c5caa0a4ef700` |
| **Service status** | ACTIVE — 1/1 RUNNING, 0 pending, rolloutState COMPLETED |
| **AzureAd env vars** | ✅ Present (`AzureAd__ClientId`, `AzureAd__TenantId`, `AzureAd__ClientSecret`) |

---

## Migration Apply Result

**Method:** Automatic — `DatabaseInitializationService.StartAsync()` calls `db.Database.MigrateAsync()` on every ECS startup. Migration runs against Aurora using production connection string from ECS env vars (not the design-time `localhost` factory).

**CloudWatch log evidence (2026-04-28 11:53 EDT):**
```
[11:53:06 INF] [NEXUS] Running EF Core migrations on startup...
[11:53:07 INF] [NEXUS] EF Core migrations complete.
```

| Field | Value |
|-------|-------|
| **Migration name** | `AddDecompositionUpgradeFields_20260427` |
| **Migration file** | `Migrations/20260428030456_AddDecompositionUpgradeFields_20260427.cs` |
| **Status** | ✅ APPLIED — no errors in logs |
| **Previous migration** | `20260415221112_AddUploadedFileUserDescription` (rollback target) |

---

## Steps Completed

| Time (EDT) | Step | Result |
|------------|------|--------|
| 07:47 | Pre-deploy snapshot captured | ✅ nexus-web:46, 1/1 RUNNING |
| 07:48 | CodeBuild triggered (`fip-nexus-build`) | ✅ Build ID: `33ad5d97-...` |
| 07:49 | Build IN_PROGRESS — PROVISIONING | — |
| 07:50 | Build IN_PROGRESS — BUILD phase | — |
| 07:50 | Build IN_PROGRESS — POST_BUILD phase | — |
| 07:51 | Build SUCCEEDED | ✅ |
| 07:51 | New image pushed to ECR | ✅ `f527f50b0ec74d4ccb0d4989bd42344345d82899` |
| 07:51 | ECS force-new-deployment triggered | ✅ |
| 07:53 | New task started — migrations applied on startup | ✅ |
| 07:53 | Service stable | ✅ 1/1 RUNNING, HEALTHY |

---

## Build & Image Details

| Field | Value |
|-------|-------|
| **Build ID** | `fip-nexus-build:33ad5d97-fd98-4da6-9959-698c6767396e` |
| **Build result** | SUCCEEDED |
| **Image tag (deployed)** | `f527f50b0ec74d4ccb0d4989bd42344345d82899` |
| **Image digest (post-deploy)** | `sha256:cb98cd1d249b6ec3b5847196c32cf00e1b9e2c2a71dd3d241acf0abf8965ee32` |
| **ECR URI** | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest` |
| **Task started at** | 2026-04-28T07:53:13 EDT |
| **Running task digest** | `sha256:cb98cd1d249b6ec3b5847196c32cf00e1b9e2c2a71dd3d241acf0abf8965ee32` ✅ matches |

---

## Post-Deploy Health Check

| Check | Result |
|-------|--------|
| Task status | RUNNING |
| Container status | RUNNING |
| Health status | HEALTHY |
| Exit code | null (no crash) |
| Desired count | 1 |
| Running count | 1 |
| Pending count | 0 |
| Image digest match | ✅ Running digest matches ECR latest |
| Migration log | ✅ `[NEXUS] EF Core migrations complete.` — no errors |
| AzureAd env vars | ✅ Present in task def (baseline :46+) |
| Stopped tasks (last 5 min) | 0 |

**Overall: ✅ HEALTHY**

---

## Rollback Plan

### ECS Rollback (to pre-deploy image)
```bash
# Source deployer credentials
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer

# Force ECS back to task definition nexus-web:46 (pre-deploy revision, image 19d2cc8)
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:46 \
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
  --query 'services[0].{taskDef:taskDefinition,runningCount:runningCount,status:status}' \
  --output json
```

> **Note:** nexus-web:46 task def has all AzureAd env vars confirmed present. Safe to roll back to.

### Migration Rollback (if schema revert needed)
```bash
# From src/FortressNexus.Web/ — with a connection string that can reach Aurora
# Roll back to the previous migration (removes columns added by ADO#2497)
dotnet ef database update AddUploadedFileUserDescription --context NexusDbContext \
  --connection "Server=<AURORA_HOST>;Database=nexus;User=<USER>;Password=<PASS>;"
```

> **Warning:** Rolling back the migration removes columns `WiType`, `PredecessorTitles`, `IsExternalDependency`, `ExternalOwner`, `WiTemplate`, `TestedByTitles` from `WorkItemRecords`, and `ExternalDependencyCount` from `ArtifactSets`. Any data in those columns will be lost. Only execute if ECS rollback alone is insufficient.

---

## What Was Deployed

- `Models/Entities/WorkItemRecord.cs` — Added: `WiType`, `PredecessorTitles`, `IsExternalDependency`, `ExternalOwner`, `WiTemplate`, `TestedByTitles`
- `Models/Entities/ArtifactSet.cs` — Added: `ExternalDependencyCount`
- `Data/NexusDbContext.cs` — JSON serialization config for list fields
- `Migrations/20260428030456_AddDecompositionUpgradeFields_20260427.cs` — EF Core migration (+ designer + snapshot)

Review: PASS (Hawkeye cycle 1, all clean)

---

_Report generated by War Machine (devops subagent) — ADO2497-DEPLOY_
