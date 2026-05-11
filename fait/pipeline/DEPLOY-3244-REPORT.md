# Deploy Report: ADO#3244 — Task Progress Timeline
**Agent:** Rhodey (DevOps subagent)  
**Date:** 2026-05-11  
**Commit:** `47282a58`  
**Result:** ✅ SUCCEEDED

---

## Pre-Deploy State

| Component | Task Def | Image |
|-----------|----------|-------|
| Blazor (fred-dev) | `fred-dev:182` | `fred-chat:c984fdb0` |
| Harness | `fait-v2-agent-harness:21` | `fait-v2-agent-harness:c984fdb0` |

- ECS cluster: `fortress-tools-cluster`
- ECS service: `fred-dev`
- AWS identity: `fortress-tools-deployer` (account 742932328420)

---

## Step 1: Pre-flight ✅

- AWS identity confirmed: `arn:aws:iam::742932328420:user/fortress-tools-deployer`
- Commit `47282a58` at HEAD of `origin/master`:
  ```
  47282a58 fix(fait#3244): cycle 2 — tool_result user-event fix, hub auth, dead field removal, CSS vars
  ```
- Current service task def confirmed: `fred-dev:182`

---

## Step 2: CodeBuild — Blazor ✅

- **Project:** `fip-fait-build`
- **Build ID:** `fip-fait-build:7fb49bd2-282c-4567-8acc-2fc6635a1979`
- **Result:** `SUCCEEDED`
- **ECR image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:kb-latest`
- **New digest:** `sha256:6913433d69c56ea26d83d593d6c498dc4e5b4407c9be07c4ebfcfc0a595a594d`

---

## Step 3: Docker Build + Push — Harness ✅

- **Source:** `/home/fredw/projects/fip/fait-v2/agent-harness`
- **Tag:** `fait-v2-agent-harness:47282a58`
- **ECR:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:47282a58`
- **Pushed digest:** `sha256:fbb4ab159b80d8eaa941f1f2512722c662887bf87ea918e58abc3e8be5d47823`
- **Build:** Used `--no-cache` as per SOUL.md directive

---

## Step 4: New Task Definitions ✅

| Component | New Task Def ARN |
|-----------|-----------------|
| Blazor | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:183` |
| Harness | `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:22` |

- Blazor: re-registered from `:182` (image tag `kb-latest` picks up new digest automatically)
- Harness: updated container image to `fait-v2-agent-harness:47282a58`

---

## Step 5: ECS Service Deploy ✅

```
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:183 \
  --force-new-deployment
```

**Deployment timeline:**
- `10:47:16` — Deployment `ecs-svc/8926385089983109532` created (PRIMARY, `:183`)
- `10:53:33` — New task `29a34dfb` started
- `10:54:03` — Task registered in ALB target group `fred-dev-tg`
- `10:54:44` — Old task `e061c881` (`:182`) stopped
- `10:54:54` — Old task deregistered + began draining
- Service reached STABLE state ✅

**Post-deploy task state:**
- Task: `29a34dfb07f04191a8a25caed7048dc7`
- Task def: `fred-dev:183`
- Status: `RUNNING`
- Health: `HEALTHY`
- Private IP: `172.31.71.61`
- ALB target state: `healthy`

---

## Step 6: HTTP Verification

- Direct ALB (`fortress-tools-alb`): HTTP 301 → HTTPS ✅ (ALB is live)
- `https://fait.dev.fortressam.ai`: HTTP 403 (Cloudflare bot challenge — expected for curl)
  - `cf-mitigated: challenge` header confirms Cloudflare is proxying correctly
  - SSL certificate valid for `fortressam.ai` ✅
  - ECS target health confirmed HEALTHY at ALB level ✅
- **ALB target group `fred-dev-tg`:** `172.31.71.61:8080` → `healthy` ✅

> Note: `fait.dev.fortressam.ai` sits behind Cloudflare with bot protection enabled.
> Programmatic curl returns 403 (CF challenge). ALB-level health check confirms
> the backend is serving correctly. This matches all previous deploy behavior.

---

## Post-Deploy State

| Component | Task Def | Revision | Status |
|-----------|----------|----------|--------|
| Blazor (fred-dev) | `fred-dev:183` | new | RUNNING / HEALTHY |
| Harness | `fait-v2-agent-harness:22` | new | registered |

---

## Rollback Commands

**Blazor:**
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:182 \
  --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

**Harness (re-register with old image):**
```bash
# Re-register fait-v2-agent-harness:21 with c984fdb0 image, or:
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service <harness-service-name> \
  --task-definition fait-v2-agent-harness:21 \
  --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Cost Impact

No new resources created. Same Fargate task size. Rolling deploy — no downtime.

---

## Lessons Learned

1. Two concurrent `force-new-deployment` requests (from `update-service` itself) caused 3 simultaneous deployment entries. The correct `:183` deployment won and stabilized cleanly.
2. Cloudflare bot protection blocks `curl` — always verify at ALB target group level for programmatic health checks.
3. CodeBuild and Docker Harness build can safely run in parallel; total deploy time ~12 minutes end-to-end.
