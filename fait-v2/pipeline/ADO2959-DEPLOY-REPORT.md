# FAIT v2 — Deploy Report: ADO#2959

**Deployer:** War Machine (Rhodey) — DevOps subagent  
**Date:** 2026-05-08  
**Commit:** `386cba2` — fix(fait#2959): remove EF provisioning redirect from Routes.razor; switch ProvisioningStatusService to AuthenticationStateProvider

---

## Pre-Deploy Snapshot

| Property | Value |
|---|---|
| Previous task def | `fait-v2:18` |
| Previous image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:b65997f` |
| ECS cluster | `fortress-tools-cluster` |
| ECS service | `fait-v2` |

---

## Git Push

- Branch `main` was already up to date with `origin/main` — no push required.
- Commit `386cba2` confirmed at HEAD.

---

## Build

- **Dockerfile:** `fait-v2/Dockerfile.debian` (monorepo root context: `/home/fredw/projects/fip/`)
- **Method:** Local Docker build + direct ECR push (fait-v2 deploy pattern — no CodeBuild project exists for fait-v2)
- **Result:** SUCCEEDED ✅
- **Image digest:** `sha256:ae2dfaed7d8a5e77bd45f2502d028decf0c67e3bd67a553b07155b3299b017ee`
- **Tags pushed to ECR:**
  - `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:386cba2` ✅
  - `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:latest` ✅

> **⚠️ Lesson learned:** Initial deploy attempt used `fip-fait-build` CodeBuild project. That project builds the OLD FAIT (fred-chat), not fait-v2. fait-v2 has NO CodeBuild project — it uses local Docker build + direct ECR push. The CodeBuild build succeeded but pushed to the `fred-chat` ECR repo, causing `CannotPullContainerError` on ECS. Corrected by building locally and pushing to `fait-v2` ECR repo.

---

## Task Definition

| Property | Value |
|---|---|
| Previous revision | `fait-v2:18` |
| New revision | `fait-v2:19` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:386cba2` |
| taskRoleArn | `arn:aws:iam::742932328420:role/fait-v2-task-role` ✅ preserved |

---

## ECS Deployment

| Property | Value |
|---|---|
| New task def | `fait-v2:19` |
| Task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2:19` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:386cba2` |
| Deployment status | PRIMARY — running=1, pending=0, failed=0 |
| Old revision | `fait-v2:18` — DRAINING |

---

## Health Check

```
curl -sk -H "Host: fait-v2.dev.fortressam.ai" https://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com/health
```

**Result: `200 OK`** ✅  
**Homepage:** `302` (redirect to login — expected) ✅

---

## Rollback Plan

If needed:
```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_REGION=us-east-1
aws ecs update-service --cluster fortress-tools-cluster --service fait-v2 --task-definition fait-v2:18 --region us-east-1
```

---

## ADO Comment

Posted to ADO#2959 — comment ID 783599

---

**Verdict: DEPLOYED ✅**
