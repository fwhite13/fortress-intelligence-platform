# Deploy Brief — FAIT v2 Sprint May 9B

**Prepared by:** Maria Hill (Pipeline Manager)
**Date:** 2026-05-09
**Service:** `fait-v2`
**Current deployed:** `fait-v2:42` (commit unknown at :42)
**HEAD commit:** `3524bcc7`
**Target:** Build image from `3524bcc7`, register new task def, deploy to ECS

---

## WIs Shipping in This Deploy

| ADO# | Title | Review Status |
|------|-------|---------------|
| #3089 | Session resumption message (harness cold-start) | ✅ PASS C1 |
| #3090 | Agent plugin admin panel + seed Marketing/Finance/Legal | ✅ PASS C1 |
| #3093 | Runtime preference detection (harness + endpoint) | ✅ PASS C2 |
| #3094 | File upload destination selector | ✅ PASS C1 |
| #3096 | Scheduled task email notifications (MS Graph) | ✅ PASS C1 |
| #3100 | Mobile responsive layout | ✅ PASS C2 |
| #3101 | Per-connector read/write permission enforcement | ✅ PASS C2 |
| #3105 | Credential/token scrubbing on CC stdout relay + harness logging | ✅ PASS C1 |
| #3106 | G3: KB write intent enforcement | ✅ PASS C1 |
| #3107 | G7: Scheduled task approval gate | ✅ PASS C1 |
| #3108 | UserEmail injection into CC context envelope (Entra UPN) | ✅ PASS C2 |
| #3109 | G4: MCP tool allowlist enforcement | ✅ PASS C1 |
| #3115 | FIRM integration SharedSecret wiring | ✅ PASS C2 |

**Also in HEAD (shipped in prior deploys, re-included):**
- #3099, #3102, #3103, #3112, #3114 — previously reviewed and in the codebase

---

## EF Migrations in This Deploy

These migrations are NEW since the last deploy and will auto-apply on startup:

| Migration | WI | Description |
|-----------|-----|-------------|
| `20260509075646_AddScheduledTaskApprovals` | #3107 | `scheduled_task_approvals` table |
| `20260509090000_AddAllowKbWriteToAgentPlugin` | #3106 | `allow_kb_write` column on `agent_plugins` |
| `20260509100000_AddAvatarUrlToUser` | #3092 | `avatar_url` column on `users` |

> ⚠️ **Note:** #3092 (Avatar NSFW check) is in HEAD but review passed C2. It IS included in the image.

**EF migrations run automatically on startup** — no manual `dotnet ef database update` needed.

---

## Build Instructions

```bash
# fait-v2 uses LOCAL Docker build — NO CodeBuild project
cd /home/fredw/projects/fip/fait-v2

# Confirm HEAD
git log --oneline -1
# Expected: 3524bcc7

# Build
docker build -f Dockerfile.debian -t fait-v2:3524bcc7 .

# Tag and push
docker tag fait-v2:3524bcc7 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:3524bcc7
aws ecr get-login-password --region us-east-1 --profile fortress-tools-deployer | \
  docker login --username AWS --password-stdin 742932328420.dkr.ecr.us-east-1.amazonaws.com
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:3524bcc7
```

---

## Task Def Registration

Use the wrapper script — NEVER raw `aws ecs register-task-definition`:

```bash
# Get current task def JSON to use as base
aws ecs describe-task-definition --task-definition fait-v2:42 \
  --profile fortress-tools-deployer --region us-east-1 \
  --query 'taskDefinition' > /tmp/fait-v2-task-def-base.json

# Update image tag in the JSON:
# 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:3524bcc7

# Register via wrapper script (auto-inherits taskRoleArn):
./scripts/ecs-register-task-def.sh --cluster fortress-tools-cluster \
  --service fait-v2 --task-def-json /tmp/fait-v2-task-def-new.json
```

**Critical:** Verify the registered task def has:
- `taskRoleArn`: `arn:aws:iam::742932328420:role/fait-v2-task-role`
- `Bedrock:AvatarModerationModelId` env var present (new in #3092 — may need to add to task def)
- `AWS:AvatarBaseUrl` env var (new in #3092 — can be empty string, will fall back to s3 URL)
- `FipMcp__BaseUrl`: `https://mcp.fortressam.ai/mcp`
- Credentials: `fortress-tools-deployer` profile only — NEVER `openclaw-bedrock`

---

## Deploy

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --task-definition fait-v2:<new_revision> \
  --profile fortress-tools-deployer \
  --region us-east-1
```

Wait for the service to stabilize (1/1 running on new task def).

---

## Pre-Deploy Snapshot / Rollback Plan

**Current stable:** `fait-v2:42`

If the new deploy fails:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --task-definition fait-v2:42 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Post-Deploy Verification

1. ECS service: `running: 1, desired: 1` on new task def revision
2. App responds: `GET https://fait-v2.dev.fortressam.ai/_framework/blazor.web.js` → 302 to auth (confirms app is live)
3. No migration errors in CloudWatch logs (`/ecs/fait-v2`)
4. Spot check: `POST /api/scheduled-tasks/approval/request` with no token → 401/403

---

## ADO Updates (Rhodey's responsibility)

After confirmed stable deploy, mark ALL shipping WIs → **Done**:
#3089, #3090, #3093, #3094, #3096, #3100, #3101, #3105, #3106, #3107, #3108, #3109, #3115

And: #3092 (also in this deploy)

---

## Deploy Report

Write to: `/home/fredw/projects/fip/fait-v2/pipeline/DEPLOY-SPRINT-MAY9B-REPORT.md`

Include: image digest, task def revision, migration results, ECS stability confirmation, rollback plan.
