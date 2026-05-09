# Deploy Report — FAIT v2 Hotfix (ADO#3117 + ADO#3118)

**Date:** 2026-05-09  
**Agent:** War Machine (Rhodey)  
**Service:** `fait-v2` on `fortress-tools-cluster`  
**Rollback target:** `fait-v2:43`

---

## Summary

Hotfix deploy for two Bug WIs:
- **ADO#3117** — fait-v2: Chat UI styling does not match FAIT v1
- **ADO#3118** — fait-v2: KB management panel not showing user's knowledge bases

---

## Deploy Steps

### 1. Git Verification
- HEAD commit verified: `9b352982` — `fix(fait#3117): c3 — remaining hex colors to vars, final 900px literals to var`

### 2. Docker Build
- Build context: `/home/fredw/projects/fip/` (monorepo root)
- Dockerfile: `fait-v2/Dockerfile.debian`
- Image: `fait-v2:9b352982`
- **Note:** Build must run from monorepo root — Dockerfile copies `shared/FipShared/` and `fait-v2/src/`
- Result: ✅ Build succeeded

### 3. ECR Push
- Tagged: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:9b352982`
- Auth: `fortress-tools-deployer` profile
- Push digest: `sha256:fbb00411f878255438a671d2247aa341c75928376d8a6831b2bc0172c2ff44ee`
- Result: ✅ Push succeeded

### 4. Task Definition Registration
- Script: `scripts/ecs-register-task-def.sh`
- Based on: `fait-v2:43`
- New revision: **`fait-v2:44`** (`arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2:44`)
- `taskRoleArn` preserved: `arn:aws:iam::742932328420:role/fait-v2-task-role`
- Result: ✅ Registered

### 5. ECS Service Update
- Service: `fait-v2` on `fortress-tools-cluster`
- Updated to: `fait-v2:44`
- Waited for stability: ✅ `services-stable` confirmed
- Final state: `running: 1, desired: 1`, single PRIMARY deployment

### 6. Health Check
- `GET https://fait-v2.dev.fortressam.ai/_framework/blazor.web.js`
- Response: `HTTP/2 302` → redirects to auth login
- Result: ✅ App live

### 7. ADO Updates
- ADO#3117 → **Closed** ✅
- ADO#3118 → **Closed** ✅

---

## No DB Changes
No EF migrations in this deploy. No database changes needed.

---

## Rollback Plan
If rollback required:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --task-definition fait-v2:43 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Result: ✅ DEPLOYED SUCCESSFULLY

| Item | Value |
|------|-------|
| Previous task def | `fait-v2:43` |
| New task def | `fait-v2:44` |
| Image | `fait-v2:9b352982` |
| ECS status | running: 1 / desired: 1 |
| App health | HTTP 302 → auth (live) |
| ADO#3117 | Closed |
| ADO#3118 | Closed |
