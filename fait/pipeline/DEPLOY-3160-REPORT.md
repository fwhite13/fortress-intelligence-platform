# Deploy Report — ADO#3160: Avatar Upload

**Date:** 2026-05-09  
**Deployer:** Rhodey (War Machine, DevOps)  
**Feature:** 2.1-B — Avatar upload: S3 storage, avatar_url persistence, display in chat header + message bubbles

---

## Summary

Deployed `fred-chat:008460d3` to ECS service `fred-dev` (cluster `fortress-tools-cluster`) as task definition `fred-dev:151`. Avatar upload feature is live.

---

## Build

| Item | Value |
|------|-------|
| Source repo | `/home/fredw/projects/fip` |
| Dockerfile | `fait/Dockerfile` |
| Tip commit | `008460d3` |
| Build flags | `--no-cache` |
| Local image tag | `fred-chat:008460d3` |
| ECR image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:008460d3` |
| ECR digest | `sha256:b445488d93d707137f1472f438ffb1825c24c1003db2265fed4339530a053ccb` |

---

## Task Definition Changes

| Item | Previous (rev 150) | New (rev 151) |
|------|--------------------|---------------|
| Image | `fred-chat:7d1688f2` | `fred-chat:008460d3` |
| `WORKSPACE_S3_BUCKET` | *(not present)* | `fortress-user-workspaces` |
| `WORKSPACE_S3_PREFIX` | *(not present)* | `""` (empty string) |
| `Fargate__ContainerName` | `fait-v2-agent-harness` ✅ | `fait-v2-agent-harness` ✅ |
| `taskRoleArn` | `fortress-tools-ecs-task-role` ✅ | `fortress-tools-ecs-task-role` ✅ |

---

## Deployment

| Step | Result |
|------|--------|
| Docker build | ✅ Success |
| ECR push | ✅ `008460d3` pushed |
| Task def registered | ✅ `fred-dev:151` |
| ECS update-service | ✅ |
| Service stabilize | ✅ RUNNING=1, PENDING=0 |
| CloudWatch startup | ✅ Clean — MCP transports healthy, no errors |

---

## Verification

- **`Fargate__ContainerName`** = `fait-v2-agent-harness` ✅ preserved
- **`WORKSPACE_S3_BUCKET`** = `fortress-user-workspaces` ✅ present in fred-dev:151
- **`WORKSPACE_S3_PREFIX`** = `""` ✅ present in fred-dev:151
- ECS service: desired=1, running=1, pending=0 ✅
- CloudWatch: No errors at startup; MCP devops/brave/m365 tools responding 200 ✅

---

## ADO

- **Work item:** ADO#3160
- **State:** Resolved
- **Comment posted:** ✅

---

## Rollback

To roll back, re-deploy `fred-dev:150`:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:150 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

_War Machine out._
