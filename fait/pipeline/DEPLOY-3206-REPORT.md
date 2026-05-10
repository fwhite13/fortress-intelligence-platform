# Deploy Report: ADO#3206 — 5.5-B: Workspace File Manager

**Date:** 2026-05-10  
**Agent:** Rhodey (War Machine) — DevOps  
**Deployer:** fortress-tools-deployer  

---

## Deploy 1 — Blazor (fred-dev)

### Image
- **Tag:** `fred-chat:32430067`
- **ECR URI:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:32430067`
- **Digest:** `sha256:b3e25cf104186551e98026647ada7f1f4028e3665a221ab517e1c5fcca89e9a2`
- **Dockerfile:** `fait/Dockerfile.debian`
- **Build:** `--no-cache` from monorepo root

### ECS
- **Previous task def:** `fred-dev:170`
- **New task def:** `fred-dev:171`
- **Task def ARN:** `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:171`
- **Service:** `fred-dev` on `fortress-tools-cluster`
- **Post-deploy status:** 1/1 running, ACTIVE ✅

### Env Update
- `Fargate__TaskDefinition` updated from `fait-v2-agent-harness:13` → `fait-v2-agent-harness:14`

### CloudWatch Startup Check ✅
- DB init: complete (all idempotent migration warnings expected)
- EF migration: NEW — `mcp_tool_call_log` LONGTEXT columns applied successfully
- App: listening on `http://[::]:8080`
- MCP tools (devops, brave, m365): all loaded successfully
- No unexpected errors

---

## Deploy 2 — Harness (fait-v2-agent-harness)

### Image
- **Tag:** `fait-v2-agent-harness:32430067`
- **ECR URI:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:32430067`
- **Digest:** `sha256:e3c0de0a05deb5cb6b411414c4bdc830d91834d8c768dc647b8dcc03d9df43a7`
- **Build context:** `/home/fredw/projects/fip/fait-v2/agent-harness/`
- **Build:** `--no-cache`

### ECS
- **Previous task def:** `fait-v2-agent-harness:13`
- **New task def:** `fait-v2-agent-harness:14`
- **Task def ARN:** `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:14`
- **Service update:** Not required — on-demand Fargate tasks pick up new revision automatically ✅

---

## Rollback

| Component | Rollback To |
|-----------|-------------|
| Blazor    | `fred-dev:170` |
| Harness   | `fait-v2-agent-harness:13` |

### Blazor Rollback Command
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:170 \
  --region us-east-1
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1
```

---

## Summary

| Step | Status |
|------|--------|
| Commit verified at `32430067` | ✅ |
| Blazor Docker build (Dockerfile.debian) | ✅ |
| Blazor ECR push | ✅ |
| fred-dev:171 task def registered | ✅ |
| ECS service updated to :171 | ✅ |
| ECS services-stable wait | ✅ |
| CloudWatch startup check | ✅ Clean |
| Harness Docker build | ✅ |
| Harness ECR push | ✅ |
| fait-v2-agent-harness:14 task def registered | ✅ |
