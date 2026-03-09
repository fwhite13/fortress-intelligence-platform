# FIRM Core Activation — Deploy Report
**Date:** 2026-03-08  
**Sprint:** FIRM Core Activation  
**Deployer:** Rhodey (devops subagent)  
**Requested by:** Maria Hill

---

## Summary

Greenfield infrastructure standup for FIRM (Fortress Intelligence RM) Blazor dashboard.

| Step | Status | Notes |
|------|--------|-------|
| ECR Repository | ✅ CREATED | `firm-web` |
| Aurora DB `firm_dev` | ✅ CREATED | Created in shared Aurora cluster |
| S3 Bucket | ⚠️ BLOCKED | `fortress-tools-deployer` lacks `s3:CreateBucket` permission |
| ECS Task Definition | ✅ REGISTERED | `firm-web:1` |
| CodeBuild Project | ⚠️ BLOCKED | `fortress-tools-deployer` lacks `codebuild:CreateProject` permission |
| GitHub Source | ✅ PUSHED | Branch `firm-deploy` on `fwhite13/fortress-tools-dotnet` |
| ECS Service | ✅ CREATED | `firm-web` on `fortress-tools-cluster`, 0/1 running (awaiting first image) |
| ALB Routing | ✅ CONFIRMED | Rule already exists: priority=15, `firm.dev.fortressam.ai` → `meetings-web-dev-tg` |
| First Build | ⏳ PENDING | CodeBuild project creation blocked — Fred action required |

---

## Resources Created

### ECR Repository
- **URI:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web`
- **Scan on push:** enabled
- **Region:** us-east-1

### Aurora DB
- **Database:** `firm_dev`
- **Cluster endpoint:** `fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com`
- **Charset:** `utf8mb4 COLLATE utf8mb4_unicode_ci`
- **Tables:** Will be auto-created by `DatabaseInitializationService` on first startup
  - `firm_users`, `firm_meetings`, `firm_meeting_participants`, `firm_meeting_transcripts`, `firm_meeting_summaries`, `firm_data_protection_keys`

### ECS Task Definition — `firm-web:1`
- **ARN:** `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:1`
- **Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:latest`
- **CPU:** 512, **Memory:** 1024
- **Network mode:** awsvpc (Fargate)
- **Execution role:** `arn:aws:iam::742932328420:role/fortress-tools-ecs-execution-role`
- **Task role:** `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role`
- **Port:** 8080
- **Log group:** `/ecs/firm-web` (auto-created on first run)

**Environment variables set:**

| Variable | Value | Source |
|----------|-------|--------|
| `ASPNETCORE_URLS` | `http://+:8080` | hardcoded |
| `ASPNETCORE_ENVIRONMENT` | `Production` | hardcoded |
| `FORTRESS_DB_HOST` | `fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com` | cloned from FAIT |
| `FORTRESS_DB_PORT` | `3306` | cloned from FAIT |
| `FORTRESS_DB_USER` | `fortress_mysql` | cloned from FAIT |
| `FORTRESS_DB_PASS` | (from Secrets Manager) | cloned from FAIT: `arn:aws:secretsmanager:us-east-1:742932328420:secret:fortress-tools/dev-db-password-9ZKFmr` |
| `FIRM_DB_NAME` | `firm_dev` | FIRM-specific |
| `Auth__EntraAuthority` | `https://login.microsoftonline.com/7152ea12.../v2.0` | cloned from FAIT |
| `Auth__EntraClientId` | `a2de171d-5bb8-4db0-87a6-d07e24b932b3` | cloned from FAIT |
| `Auth__EntraClientSecret` | `9V-8Q~...` | cloned from FAIT |
| `Auth__CookieDomain` | `.fortressam.ai` | FIRM-specific |
| `Firm__S3Bucket` | `firm-recordings-dev` | FIRM-specific (bucket not yet created — Fred action) |
| `Firm__EcsCluster` | `arn:aws:ecs:us-east-1:742932328420:cluster/fortress-tools-cluster` | FIRM-specific |
| `Firm__BotCallbackSecret` | `d4d5a4a7e055f6e8f4f0eb67a8c54735` | Generated fresh (openssl rand -hex 16) |
| `AWS__Region` | `us-east-1` | hardcoded |
| `UseStubAuth` | `false` | hardcoded |

### ECS Service — `firm-web`
- **Cluster:** `fortress-tools-cluster`
- **Task definition:** `firm-web:1`
- **Desired count:** 1
- **Launch type:** FARGATE
- **Subnets:** `subnet-08e1d4f1b5530f39e`, `subnet-051bfcf5b07661809`
- **Security group:** `sg-0fb53615b1eb4a175`
- **Target group:** `meetings-web-dev-tg` (`arn:...:targetgroup/meetings-web-dev-tg/7a7e9af531f05a53`)
- **Status:** ACTIVE, running=0 (awaiting first image in ECR)
- **Health check grace period:** 60 seconds

### ALB Routing
- **Rule priority:** 15 (pre-existing)
- **Host header:** `firm.dev.fortressam.ai`
- **Target group:** `meetings-web-dev-tg` (port 8080)
- **Note:** ALB URL is `firm.dev.fortressam.ai` (not `meetings.dev.fortressam.ai` as originally specified — this is the correct existing routing)

### GitHub Source
- **Repo:** `github.com/fwhite13/fortress-tools-dotnet`
- **Branch:** `firm-deploy` (29 files, commit `f684a314`)
- **Path in repo:** `fortress-intelligence-rm/`
- **Buildspec:** `fortress-intelligence-rm/buildspec.yml`
- **Buildspec behavior:** `cd fortress-intelligence-rm` → dotnet build → docker build -f Dockerfile.debian → push to ECR → ECS update-service

---

## Pre-Deploy Snapshot
N/A — greenfield deployment, no existing service to roll back.

---

## Rollback Plan

Since this is greenfield, "rollback" means taking FIRM down:

```bash
# Option 1: Stop service (keep task def and ECS service)
aws ecs update-service --cluster fortress-tools-cluster --service firm-web \
  --desired-count 0 --region us-east-1

# Option 2: Delete service entirely
aws ecs delete-service --cluster fortress-tools-cluster --service firm-web \
  --force --region us-east-1

# Option 3: Deregister task def
aws ecs deregister-task-definition --task-definition firm-web:1 --region us-east-1
```

---

## Fred Action Required — BLOCKING

### 1. Create S3 Bucket `firm-recordings-dev`
The deployer IAM user lacks `s3:CreateBucket`. Fred (or an admin) must run:

```bash
source /home/fredw/.openclaw/workspace/ai/projects/fortress_tools/.env.deployer
# Use an IAM user/role with S3 admin permissions:
aws s3api create-bucket --bucket firm-recordings-dev --region us-east-1
aws s3api put-bucket-versioning --bucket firm-recordings-dev --versioning-configuration Status=Enabled
aws s3api put-public-access-block --bucket firm-recordings-dev \
  --public-access-block-configuration "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"
```

Also add S3 permissions to the ECS task role `fortress-tools-ecs-task-role`:
```json
{
  "Effect": "Allow",
  "Action": ["s3:GetObject", "s3:PutObject", "s3:DeleteObject", "s3:GetObjectUrl"],
  "Resource": "arn:aws:s3:::firm-recordings-dev/*"
}
```

### 2. Create CodeBuild Project `fip-firm-build`
The deployer lacks `codebuild:CreateProject`. Create via Console or admin CLI:

```bash
aws codebuild create-project \
  --name fip-firm-build \
  --source '{"type":"GITHUB","location":"https://github.com/fwhite13/fortress-tools-dotnet","buildspec":"fortress-intelligence-rm/buildspec.yml","gitCloneDepth":1}' \
  --source-version "firm-deploy" \
  --artifacts '{"type":"NO_ARTIFACTS"}' \
  --environment '{"type":"LINUX_CONTAINER","image":"aws/codebuild/standard:7.0","computeType":"BUILD_GENERAL1_SMALL","privilegedMode":true}' \
  --service-role "arn:aws:iam::742932328420:role/codebuild-fip-fait-build-service-role" \
  --region us-east-1
```
**Note:** Use the same service role as `fip-fait-build` — it already has ECR push + ECS update permissions. Also needs `codebuild:CreateWebhook` for auto-build on push (optional).

### 3. Trigger First Build
After project creation:
```bash
source /home/fredw/.openclaw/workspace/ai/projects/fortress_tools/.env.deployer
aws codebuild start-build --project-name fip-firm-build --region us-east-1 --profile fortress-tools-deployer
```
The deployer user **CAN** `start-build` — only `create-project` is blocked.

### 4. Entra App Registration — Add Redirect URI
FIRM uses the **same Entra app registration as FAIT** (cloned credentials). Fred must add FIRM's redirect URI:
- **Redirect URI to add:** `https://firm.dev.fortressam.ai/signin-oidc`
- **Where:** Azure Portal → App Registrations → `a2de171d-5bb8-4db0-87a6-d07e24b932b3` → Authentication → Add redirect URI

### 5. Merge `firm-deploy` → `main` on GitHub
The FIRM code is on branch `firm-deploy`. After validating the build works, merge to main:
```bash
cd /home/fredw/.openclaw/workspace/fortress-tools-dotnet
git checkout firm-deploy
git push origin firm-deploy  # already done
# Then create PR on GitHub and merge
```

### 6. VP Bot Environment Variables
The VP bot ECS task definition needs these env vars for FIRM callbacks:
```
FIRM_API_URL=https://firm.dev.fortressam.ai
BOT_CALLBACK_SECRET=d4d5a4a7e055f6e8f4f0eb67a8c54735
```
(Must match `Firm__BotCallbackSecret` set in FIRM task def.)

---

## Post-First-Build Verification

Once build succeeds and ECS service is running:

```bash
# Check service health
aws ecs describe-services --cluster fortress-tools-cluster --services firm-web \
  --region us-east-1 --profile fortress-tools-deployer \
  --query 'services[0].{running:runningCount,desired:desiredCount}' --output json

# Check HTTPS endpoint (requires DNS to resolve firm.dev.fortressam.ai)
curl -I https://firm.dev.fortressam.ai/

# Check CloudWatch logs
aws logs tail /ecs/firm-web --since 5m --region us-east-1
```

Expected on first successful start:
- `DatabaseInitializationService` logs: `firm_users`, `firm_meetings`, etc. tables created
- `Production authentication configured (Entra OIDC)`
- Blazor application starts on port 8080

---

## CodeBuild build ID and status
**Status:** NOT STARTED — `fip-firm-build` project not yet created (CodeBuild IAM limitation).

## Running task image digest
**Status:** N/A — no image in ECR yet, service desired=1 but running=0.

---

_Report generated: 2026-03-08 by Rhodey (devops subagent)_
