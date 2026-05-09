# Deploy Report — ADO#3145
**Task Mode Toggle + mode_switch SSE Event**

**Date:** 2026-05-09  
**Deployed by:** War Machine (Rhodey) — DevOps subagent  
**Commit:** `1261e3f7`  
**Service:** `fred-dev` on `fortress-tools-cluster`

---

## What Was Deployed

Feature ADO#3145 — Task Mode toggle (user-initiated) and `mode_switch` SSE event (assistant-initiated task mode indicator) in the FAIT chat UI.

**Commit message:** `feat(fait#3145): task mode toggle + mode_switch SSE event`

---

## Deployment Steps

### 1. ✅ Pre-flight
- AWS credentials: `fortress-tools-deployer` confirmed
- ECR repo `fred-chat` verified

### 2. ✅ Docker Build
- Source: `/home/fredw/projects/fip/` (monorepo root)
- Dockerfile: `fait/Dockerfile`
- Image: `fred-chat:1261e3f7`
- Build: `--no-cache`, completed successfully
- Output digest: `sha256:bf0558513544f4c6435045e75d28534461c246988ef4785e9729419b995ece98`

### 3. ✅ ECR Push
- Tag: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:1261e3f7`
- Push digest: `sha256:154577ebec925f2d85bb4e0e049da43684fcb1883d522c0b2507f5b44907f567`

### 4. ✅ Task Definition Registered
- Previous: `fred-dev:135` (image `fred-chat:a890d5c1`)
- New: `fred-dev:136` (image `fred-chat:1261e3f7`)
- ARN: `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:136`
- All env vars preserved (Fargate__* vars, taskRoleArn, secrets)

### 5. ✅ ECS Service Updated
- Cluster: `fortress-tools-cluster`
- Service: `fred-dev`
- Task def: `fred-dev:136`
- Force new deployment: yes
- Final state: **1 RUNNING, 0 PENDING, 0 OLD** — stable

### 6. ✅ CloudWatch Logs — Clean Startup
- No errors on startup
- App listening on `http://[::]:8080`
- MCP tools loaded: devops, brave, m365
- Hosting environment: Development

---

## Rollback

If needed:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:135 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## ADO Update

- ADO#3145 → **Resolved**
- Comment: "Deployed fred-chat:1261e3f7, fred-dev:136. ECS stable (1 running, 0 pending). Task mode toggle + mode_switch indicator live."
