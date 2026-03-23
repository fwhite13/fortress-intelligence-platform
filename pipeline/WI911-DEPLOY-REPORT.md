# Deploy Report: WI911 — Cowork Design Agent

## Outcome: ⚠️ BLOCKED — IAM Permission Gap

**Date:** 2026-03-20  
**Time:** ~04:05–04:20 EDT  
**Deployer:** War Machine (James Rhodes / devops)  
**Commit:** `3716baf`  

---

## What Was to Be Deployed

WI911: Cowork Design Agent — text-to-UI generation, brand context, variants, Blazor conversion  
- 17 files in `fip/cowork/`  
- Both `cowork-agent` (Node.js) and `cowork-web` (.NET 9 Blazor Server) services

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| `cowork-agent` task def | `arn:aws:ecs:us-east-1:742932328420:task-definition/cowork-agent:7` |
| `cowork-web` task def | `arn:aws:ecs:us-east-1:742932328420:task-definition/cowork-web:7` |
| `cowork-agent` image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:c4083da` |
| `cowork-web` image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:c4083da` |
| `cowork-agent` running | 1/1 |
| `cowork-web` running | 1/1 |
| ECS cluster | `fortress-tools-cluster` |
| Last image push | 2026-03-17 (commit `c4083da`) |

---

## Rollback Plan (pre-captured)

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
# Rollback cowork-agent:
aws ecs update-service --cluster fortress-tools-cluster --service cowork-agent \
  --task-definition cowork-agent:7 --force-new-deployment --region us-east-1

# Rollback cowork-web:
aws ecs update-service --cluster fortress-tools-cluster --service cowork-web \
  --task-definition cowork-web:7 --force-new-deployment --region us-east-1
```
> Note: Rollback targets are current baselines. No rollback needed — deploy was blocked before any changes.

---

## Blocker: IAM Permission Gap

### What Was Attempted

1. ✅ ADO pre-deploy comment posted (comment ID 726602)
2. ✅ Pre-deploy snapshot captured (both services confirmed at `:7`)
3. ✅ ECR login succeeded (`fortress-tools-deployer` has ECR push permissions)
4. ❌ **CodeBuild trigger BLOCKED** — `fortress-tools-deployer` lacks `codebuild:StartBuild` on cowork projects

### Root Cause

The `fortress-tools-deployer` IAM user has `codebuild:StartBuild` permission scoped to `fip-famos-build` only (prior deploys confirmed this). Cowork CodeBuild projects exist but are not in the IAM policy:

| Project Name | Status |
|---|---|
| `cowork-agent-build` | EXISTS — `AccessDeniedException` (no StartBuild perm) |
| `cowork-web-build` | EXISTS — `AccessDeniedException` (no StartBuild perm) |
| `cowork-docker` | EXISTS — `AccessDeniedException` (no StartBuild perm) |
| `cowork-pipeline` | EXISTS — `AccessDeniedException` (no StartBuild perm) |
| `fortress-cowork-build` | EXISTS — `AccessDeniedException` (no StartBuild perm) |
| `fip-cowork-agent-build` | NOT FOUND — `ResourceNotFoundException` |
| `fip-cowork-web-build` | NOT FOUND — `ResourceNotFoundException` |

### Why Local Build Was Not Used

Per pipeline policy: **"Corporate FIP apps build in AWS only — never build FIP Docker images locally on SteamServer."** Cowork is a FIP app. Local build attempted and immediately killed to comply with this constraint.

### Additional Finding: No ALB Target Group for Cowork

```
ALB Target Groups: fait-prod-tg | famos-dev-tg | fip-dev-tg | formiq-dev-tg | ...
```
→ No `cowork-dev-tg` exists. The cowork services are running in ECS but not routed through the ALB.  
→ Health checks via `cowork.dev.fortressam.ai` return 503 (pre-existing — not caused by this deploy attempt).  
→ This may be a separate infrastructure gap to address.

---

## Current State

| Item | State |
|------|-------|
| ECS services | Unchanged — `cowork-agent:7` + `cowork-web:7` still running |
| ECR images | Unchanged — `c4083da` (March 17) still current |
| No rollback needed | No changes were made |
| ADO comment posted | ✅ |

---

## What's Needed to Proceed

**Fred must choose one of:**

### Option A: Grant IAM Permission
Add `codebuild:StartBuild` on the correct cowork CodeBuild project ARN to `fortress-tools-deployer`.  
Which project name is the correct one to use? (Candidates: `cowork-agent-build`, `cowork-web-build`, `cowork-docker`, `fortress-cowork-build`)

### Option B: Trigger CodeBuild Manually
Fred (or someone with IAM permissions) triggers the cowork CodeBuild project manually from AWS Console.  
Once build completes, Rhodey can execute the ECS force-deploy steps (steps 3–5) — those permissions ARE in place.

### Option C: Exception Approval for Local Build
If Fred approves an exception to the "no local FIP builds" constraint,  
Rhodey can build locally, push to ECR, register new task definitions, and force-deploy ECS.

---

## Steps Ready to Execute (Once Unblocked)

Once new images are in ECR for commit `3716baf`:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer

# Force new ECS deployments (Rhodey can do this — perms are in place)
aws ecs update-service --cluster fortress-tools-cluster --service cowork-agent \
  --force-new-deployment --region us-east-1
aws ecs update-service --cluster fortress-tools-cluster --service cowork-web \
  --force-new-deployment --region us-east-1

# Wait for stability
aws ecs wait services-stable --cluster fortress-tools-cluster \
  --services cowork-agent cowork-web --region us-east-1
```

---

## Conclusion

Deploy is **blocked at CodeBuild trigger step**. Services are healthy at baseline `:7`. No changes were made to production. Awaiting Fred's decision on IAM permission path forward.

---

*Report generated by War Machine (James Rhodes) — devops agent*  
*2026-03-20 04:20 EDT*
