# Pipeline Plan: ADO#3525 — PROD INCIDENT: fait-prod Clean Rebuild

**Priority:** P1 — PRODUCTION INCIDENT  
**Risk Level:** CRITICAL  
**Pipeline Path:** EMERGENCY — Build → Deploy only (no review/security gates; this is a clean rollback to a known-good commit)  
**Dispatched:** 2026-05-19  
**WI:** ADO#3525  

---

## Situation

fait-prod is currently running ECR image `5b393b39` (task def `fait-prod:49`) which contains evolution/harness schema that does NOT exist in the `fait_prod` database. The attempted rollback to `fait-prod:45` (image `3b7177b4`) failed because that ECR image no longer exists.

**Prod is up but serving contaminated code. Rob Nethery (rnethery@fortressinsurance.com) cannot use the assistant.**

---

## Acceptance Criteria

1. A clean Docker image built from commit `c3914307a26c0f3c0ef9e0039009129964f237f5` is pushed to ECR as `fred-chat:fait-prod-v1-stable`
2. A new `fait-prod` task definition is registered using env vars copied from `fait-prod:45`
3. The `fait-prod` ECS service is updated to that new task def with `--force-new-deployment`
4. `https://fait.fortressam.ai` loads without assistant setup spinner
5. Deploy Report produced with rollback plan

---

## Technical Context

- **Target commit:** `c3914307a26c0f3c0ef9e00399009129964f237f5` — last clean v1 commit before harness code landed
- **Repo:** `/home/fredw/projects/fip/fait`
- **Branch:** `fait-prod` (this commit is on it)
- **ECR repo:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat`
- **Target image tag:** `fred-chat:fait-prod-v1-stable`
- **Current failing task def:** `fait-prod:49` (image `5b393b39`)
- **Source env vars:** `fait-prod:45` — copy ALL env vars exactly from this revision
- **ECS service:** `fait-prod` in `us-east-1`
- **Credentials:** Use `fortress-tools-deployer` — NEVER `openclaw-bedrock`
- **Dockerfile:** Use the standard `Dockerfile` in the fait repo root (the one used for prod builds)

---

## Build Instructions

**This is NOT a CodeBuild job.** CodeBuild builds from `refs/heads/master` and would pick up contaminated code. This requires a **local Docker build** from the specific commit.

```bash
# 1. Checkout the target commit
cd /home/fredw/projects/fip/fait
git checkout c3914307a26c0f3c0ef9e0039009129964f237f5

# 2. Build the image locally (use --no-cache to avoid stale layers)
docker build --no-cache -t fred-chat:fait-prod-v1-stable .

# 3. Tag for ECR
docker tag fred-chat:fait-prod-v1-stable 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:fait-prod-v1-stable

# 4. Login to ECR and push
aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin 742932328420.dkr.ecr.us-east-1.amazonaws.com
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:fait-prod-v1-stable

# 5. Get env vars from fait-prod:45
aws ecs describe-task-definition --task-definition fait-prod:45 --region us-east-1

# 6. Register new task def (clone fait-prod:45, update image to new ECR tag)
# 7. Update fait-prod ECS service to new task def + --force-new-deployment
# 8. Monitor until service is stable (running task count = desired)
# 9. Verify https://fait.fortressam.ai loads cleanly
```

---

## Rollback Plan (pre-deploy)

Before deploying: note the current running task def revision (`:49`). If new deploy fails or site is broken, revert service to the most recent working revision available. Since `:45` image is gone, rollback would require re-pushing the same `fait-prod-v1-stable` image or escalating to Fred.

---

## Known Issues to Watch

- **Aurora MySQL 8.0.40 does NOT support ADD COLUMN IF NOT EXISTS** — but this is a clean v1 build, not a migration, so this shouldn't apply
- **Git checkout to a detached HEAD** — ensure Dockerfile and all assets are at the correct commit
- **Docker build context** — confirm which Dockerfile to use; check if there's a `Dockerfile.prod` vs just `Dockerfile`

---

## ADO Updates Required

- Mark WI #3525 → Active when build starts  
- Mark WI #3525 → Done when deploy verified  
- Add comment with ECR digest, task def revision, and verification result

