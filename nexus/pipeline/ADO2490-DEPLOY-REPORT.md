# Deploy Report — ADO#2490
**nexus-web | IWiClassifier + WiClassifierService**

| Field | Value |
|-------|-------|
| **Date** | 2026-04-27 |
| **Engineer** | War Machine (Rhodey) |
| **Commit** | `19d2cc8` — `feat(nexus#2490): add IWiClassifier interface and WiClassifierService` |
| **Build ID** | `fip-nexus-build:8bd05777-8732-4b09-92f6-1d5e9e496ff7` |
| **Result** | ✅ HEALTHY |

---

## Pre-Deploy Snapshot (Rollback Baseline)

| Field | Value |
|-------|-------|
| **Task Definition** | `nexus-web:46` |
| **Task Def ARN** | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:46` |
| **Image (pre-deploy)** | `nexus-web:latest` → `a09ba8e0c7fbf0b4e92ce7786b10afaca0f2eb51` |
| **Pre-deploy digest** | `sha256:1f9e592b04e685c3b9317fcf01af9fdcf95eae6500053c4d6712440af5a871cf` |
| **Service status** | ACTIVE — 1/1 RUNNING, 0 pending, rolloutState COMPLETED |
| **AzureAd env vars** | ✅ Present (`AzureAd__ClientId`, `AzureAd__TenantId`, `AzureAd__ClientSecret`) |

---

## Steps Completed

| Time (EDT) | Step | Result |
|------------|------|--------|
| 22:51 | Pre-deploy snapshot captured | ✅ nexus-web:46, 1/1 RUNNING |
| 22:51 | CodeBuild triggered (`fip-nexus-build`) | ✅ Build ID: `fip-nexus-build:8bd05777-...` |
| 22:52 | Build IN_PROGRESS — PROVISIONING | — |
| 22:52 | Build IN_PROGRESS — BUILD phase | — |
| 22:53 | Build IN_PROGRESS — POST_BUILD phase | — |
| 22:53 | Build SUCCEEDED | ✅ |
| 22:53 | New image pushed to ECR | ✅ `19d2cc8f9393dfd5ce44ec3ae4bb742912abdbf3` |
| 22:54 | ECS force-new-deployment triggered | ✅ |
| 22:57 | Service stable | ✅ 1/1 RUNNING |

---

## Build & Image Details

| Field | Value |
|-------|-------|
| **Build ID** | `fip-nexus-build:8bd05777-8732-4b09-92f6-1d5e9e496ff7` |
| **Build result** | SUCCEEDED |
| **Image tag (deployed)** | `19d2cc8f9393dfd5ce44ec3ae4bb742912abdbf3` |
| **Image digest (post-deploy)** | `sha256:b6f5d11dae307d082d7f563fdfd9c81c92b6a3c4c7f89f1e949c5caa0a4ef700` |
| **ECR URI** | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest` |
| **Task started at** | 2026-04-27T22:55:59 EDT |

---

## Post-Deploy Health Check

| Check | Result |
|-------|--------|
| Task status | RUNNING |
| Container status | RUNNING |
| Exit code | null (no crash) |
| Desired count | 1 |
| Running count | 1 |
| Pending count | 0 |
| Rollout state | COMPLETED (single PRIMARY deployment) |
| Stopped tasks (last 5 min) | 0 |

**Overall: ✅ HEALTHY**

---

## Rollback Plan

If rollback to pre-deploy state is needed, execute these exact commands:

```bash
# Source deployer credentials
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer

# Force ECS back to task definition nexus-web:46 (pre-deploy revision)
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

> **Note:** nexus-web:46 task def has AzureAd env vars confirmed present. Safe to roll back to.

---

## What Was Deployed

- `Services/IWiClassifier.cs` — new interface
- `Services/WiClassifierService.cs` — new service implementation
- `Program.cs` — `AddScoped<IWiClassifier, WiClassifierService>()` DI registration

Review: PASS (Hawkeye cycle 1, all clean)
