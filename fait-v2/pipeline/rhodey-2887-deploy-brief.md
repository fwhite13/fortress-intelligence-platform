# DEPLOY BRIEF — ADO#2887 — FORGE KB Integration Service
**Sprint 3, Lane 1 | FAIT v2 Epic #2835**
**Agent:** Rhodey (War Machine)
**Date:** 2026-05-07

---

## What to Deploy

FAIT v2 — Blazor Server app (`FortressAI.V2.Web`)

**Commit:** `77bcb20` (on `main`, already pushed to origin)
**Build:** CodeBuild `fip-fait-v2-build` (AWS CodeBuild, builds from GitHub origin/main)
**Service:** ECS `fait-v2` on cluster `fortress-tools-cluster`
**ECR Repo:** `fait-v2` (in account 742932328420, us-east-1)

---

## ⚠️ IMPORTANT: Service Currently at Desired Count 0

The `fait-v2` ECS service was created in the Sprint 2 infra deploy but is currently halted at `desired-count=0` because `fait-v2/postgres-master` secret is missing from Secrets Manager (Fred is creating it).

**Your deploy should:**
1. Trigger a CodeBuild build from the current `main` (commit `77bcb20`)
2. Wait for build to push the new ECR image
3. Register a new task definition revision (if needed — the image tag will have changed)
4. Update the ECS service to use the new task def AND set desired-count=1
5. Wait for service stability
6. Health check: `curl https://fait-v2.dev.fortressam.ai/health` (or direct ALB if DNS not propagated yet)

---

## Pre-Deploy Snapshot

- **Service:** `arn:aws:ecs:us-east-1:742932328420:service/fortress-tools-cluster/fait-v2`
- **Task Def:** `fait-v2:1` (registered in Sprint 2 infra deploy)
- **Current image:** Not running (desired-count=0)
- **Target group:** `arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fait-v2-dev-tg/b81255eae56c643c`
- **Log group:** `/ecs/fait-v2`

---

## CodeBuild Project

```bash
aws codebuild start-build \
  --project-name fip-fait-v2-build \
  --profile fortress-tools-deployer \
  --region us-east-1
```

Wait for build to complete (SUCCEEDED). Then verify the new ECR image digest.

---

## Credentials

Always use `--profile fortress-tools-deployer` for all AWS commands.

```bash
# Load deployer credentials if needed
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_REGION=us-east-1
```

---

## ECS Task Definition

The task def `fait-v2:1` was pre-registered. After CodeBuild produces a new ECR image, you may need to register `fait-v2:2` with the updated image URI, or use the `:latest` tag if the existing task def already uses `:latest`.

Check the existing task def container image:
```bash
aws ecs describe-task-definition --task-definition fait-v2:1 \
  --profile fortress-tools-deployer --region us-east-1 \
  --query 'taskDefinition.containerDefinitions[0].image'
```

If it uses `:latest` tag → just force a new deployment. If pinned to a digest → register a new revision with the new digest.

---

## Bring Service Up

Once build complete and image pushed:
```bash
# Force new deployment (pulls latest ECR image)
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --force-new-deployment \
  --desired-count 1 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

Wait for stability:
```bash
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fait-v2 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Health Check

After stability:
```bash
curl -s https://fait-v2.dev.fortressam.ai/health
# Expected: 200 OK or {"status":"healthy"}
```

If DNS not yet propagated, check via ALB directly:
```bash
curl -s -H "Host: fait-v2.dev.fortressam.ai" \
  http://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com/health
```

Also check CloudWatch logs for startup errors:
```bash
aws logs tail /ecs/fait-v2 --since 5m \
  --profile fortress-tools-deployer --region us-east-1
```

Look for: EF Core migrations complete, no secret resolution errors, Application started.

---

## ⚠️ Wait Condition

If `fait-v2/postgres-master` secret still doesn't exist when you try to bring the service up, the tasks will fail to start. Check secret exists first:
```bash
aws secretsmanager describe-secret \
  --secret-id "fait-v2/postgres-master" \
  --profile fortress-tools-deployer \
  --region us-east-1 2>&1
```

If it returns `ResourceNotFoundException` → hold. DM Maria and Fred that you're waiting on the secret.
If it returns the secret metadata → proceed.

---

## ADO Work Item Update (MANDATORY)

After deploy:
```bash
mcporter call devops.add_comment --args '{"project":"Fortress","id":2887,"text":"**[Rhodey — DEPLOY]**\nTask def: fait-v2:{N}. ECS service fait-v2 running. Health: {result}. CloudWatch: clean."}'
```

---

## Rollback Plan

If deploy fails or health check fails:
```bash
# Set desired-count back to 0 to stop thrashing
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --desired-count 0 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

Then report back to Maria with exact error from CloudWatch logs.

---

## Deliverables

1. Deploy Report at `~/projects/fip/fait-v2/pipeline/ADO2887-DEPLOY-REPORT.md`
2. ADO comment on #2887
3. Report back to Maria with outcome
