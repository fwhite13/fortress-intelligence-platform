# Deploy Report — ADO#3147

**Date:** 2026-05-09  
**Deployer:** War Machine (Rhodey) — DevOps subagent  
**Priority:** P0 Hotfix  
**ADO WI:** [#3147 — GuidFormat missing on MySqlConnectionStringBuilder — InvalidCastException on user_sessions query](https://dev.azure.com/FortressAffinityGroup/74c75814-3f18-429a-be96-5c068deb0632/_workitems/edit/3147)

---

## What Was Deployed

**Fix:** `GuidFormat = MySqlGuidFormat.None` added to both `MySqlConnectionStringBuilder` instances in `Program.cs`:
1. Main `AppDbContext` connection string builder (`fredConnectionString`)
2. `SharedKeyRingDbContext` connection string builder (`keyRingCsb`)

**Commit:** `6ed90f0c`  
**Message:** `fix(fait#3147): add GuidFormat=None to both MySqlConnectionStringBuilder instances — fixes EnsureRunningAsync GUID query crash`

---

## Deployment Steps

### 1. Build
```
docker build --no-cache -f fait/Dockerfile -t fred-chat:6ed90f0c .
```
- Build result: ✅ SUCCESS
- Image digest: `sha256:4b0b69056f3d52657da7d996dd8e5049835c35928529a2b8b9bb0cf69982b322`

### 2. ECR Push
```
docker tag fred-chat:6ed90f0c 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:6ed90f0c
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:6ed90f0c
```
- Push result: ✅ SUCCESS
- ECR tag: `fred-chat:6ed90f0c`
- Digest: `sha256:4b0b69056f3d52657da7d996dd8e5049835c35928529a2b8b9bb0cf69982b322`

### 3. Task Definition
- Source: `fred-dev:136` (image `fred-chat:1261e3f7`)
- New: `fred-dev:137` (image `fred-chat:6ed90f0c`)
- All Fargate env vars preserved (Fargate__*, auth, KB, DB)
- `taskRoleArn` preserved: `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role`
- Registered at: 2026-05-09T18:02:35Z

### 4. ECS Deploy
```
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:137 --force-new-deployment
```
- Service: `fred-dev` on `fortress-tools-cluster`
- New task ID: `b8aba4e89eff4542be0ff4dd25ab6187`
- Health: ✅ HEALTHY
- Status: ✅ RUNNING (desired=1, running=1, pending=0)

### 5. Log Verification
- Log stream: `ecs/fred/b8aba4e89eff4542be0ff4dd25ab6187`
- `InvalidCastException`: ✅ NOT FOUND
- `Unable to cast object of type 'System.Guid'`: ✅ NOT FOUND
- Startup: ✅ CLEAN — `Application started` confirmed
- Schema migrations: expected idempotent `fail:` entries (columns already exist) — normal

### 6. ADO Update
- State: **Resolved**
- Comment: "Deployed fred-chat:6ed90f0c, fred-dev:137. ECS stable. GuidFormat=None applied to both builders."

---

## Summary

| Field | Value |
|-------|-------|
| Previous image | `fred-chat:1261e3f7` |
| New image | `fred-chat:6ed90f0c` |
| Previous task def | `fred-dev:136` |
| New task def | `fred-dev:137` |
| ECS cluster | `fortress-tools-cluster` |
| ECS service | `fred-dev` |
| Health status | HEALTHY |
| Bug fixed | `InvalidCastException` in `EnsureRunningAsync` / `user_sessions` GUID query |
| ADO#3147 | ✅ Resolved |

---

## Rollback

If regression observed, redeploy prior revision:
```bash
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:136 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```
