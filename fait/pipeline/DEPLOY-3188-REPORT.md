# Deploy Report: ADO#3188 — 4.2-A: Harness read_memory + write_memory tools

**Date:** 2026-05-10
**Agent:** Rhodey (DevOps)
**Commit:** `124d2388a477b7dd9e004965497ac364c83414e8`

---

## Deployment Type
AWS ECS — Blazor app (`fred-dev` service) + Harness on-demand task def (`fait-v2-agent-harness`)

---

## Pre-Deploy Snapshot

| Service | Previous Task Def | Previous Image |
|---------|------------------|----------------|
| `fred-dev` | `fred-dev:162` | `fred-chat:72bc61af` |
| `fait-v2-agent-harness` (task def only) | `fait-v2-agent-harness:11` | `fait-v2-agent-harness:d66ababa` |

---

## Deployment 1: Blazor App (`fred-dev`)

### Steps

1. ✅ Commit verified at HEAD: `124d2388`
2. ✅ Docker build: `docker build --no-cache -f fait/Dockerfile.debian -t fred-chat:124d2388 .`
   - Build succeeded, image: `sha256:3b3cfec5876644b5d697cc0c36dbbcdfececc9c786fa286fd8ba8ec76907a061`
3. ✅ ECR push: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:124d2388`
   - ECR digest: `sha256:b3cd8c43a2d0c4c281616668ff8f89937b0003a83452be0658502f0fb3008681`
4. ✅ Task def registered: `fred-dev:163` (cloned from `fred-dev:162`, image updated)
   - taskRoleArn: `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role`
5. ✅ Service updated: `aws ecs update-service --task-definition fred-dev:163 --force-new-deployment`
6. ✅ `aws ecs wait services-stable` — completed successfully
7. ✅ Verification: running task shows `fred-chat:124d2388` — RUNNING (1/1)

### Result
| Field | Value |
|-------|-------|
| Previous task def | `fred-dev:162` |
| New task def | `fred-dev:163` |
| Image tag | `fred-chat:124d2388` |
| ECR digest | `sha256:b3cd8c43a2d0c4c281616668ff8f89937b0003a83452be0658502f0fb3008681` |
| Running count | 1/1 |
| Status | ✅ STABLE |

---

## Deployment 2: Harness (`fait-v2-agent-harness`)

> Note: The harness has no persistent ECS service — it runs on-demand per task spawn.
> "Deploying" means registering a new task definition revision that future task launches will use.

### Steps

1. ✅ Commit verified at HEAD: `124d2388`
2. ✅ Docker build: `docker build --no-cache -t fait-v2-agent-harness:124d2388 .` (from `fait-v2/agent-harness/`)
   - Build succeeded, image: `sha256:1313861112a64f1d5cf4baf6323fcb6a7c6b2cc864ca55f30e7e14eec87148b4`
3. ✅ ECR push: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:124d2388`
   - ECR digest: `sha256:7b863b038a4580185e551fd95f6c9472ef37b20251ed4e76d555ff22b892e5b9`
4. ✅ Task def registered: `fait-v2-agent-harness:12` (cloned from `:11`, image updated)
   - CPU: 512, Memory: 1024
5. ✅ No ECS service update needed — harness is on-demand

### Result
| Field | Value |
|-------|-------|
| Previous task def | `fait-v2-agent-harness:11` |
| New task def | `fait-v2-agent-harness:12` |
| Image tag | `fait-v2-agent-harness:124d2388` |
| ECR digest | `sha256:7b863b038a4580185e551fd95f6c9472ef37b20251ed4e76d555ff22b892e5b9` |
| Status | ✅ REGISTERED — ready for on-demand task launch |

---

## Rollback

### fred-dev
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:162 \
  --region us-east-1
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1
```

### fait-v2-agent-harness
Previous task def `fait-v2-agent-harness:11` is still registered — any new task launches can be pointed to `:11` if needed.

---

## Summary

Both ADO#3188 images built, pushed, and deployed:
- **Blazor (`fred-dev`):** `fred-chat:124d2388` → `fred-dev:163` → STABLE ✅
- **Harness:** `fait-v2-agent-harness:124d2388` → `fait-v2-agent-harness:12` → REGISTERED ✅

No DB migrations. No new env vars. Clean deploy.
