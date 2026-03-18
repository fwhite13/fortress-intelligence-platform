# WI824 Deploy Report — FfE S8: Named Range Registration

**Date:** 2026-03-17  
**Deployer:** War Machine (James Rhodes) — `devops`  
**Targets:** fred-dev + fait-prod  
**Status:** ✅ DEPLOYED

---

## Pre-Deploy Snapshot

| Service    | Task Definition         | Running | Image                                        |
|------------|------------------------|---------|----------------------------------------------|
| fred-dev   | fred-dev:118            | 1       | 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:kb-latest |
| fait-prod  | fait-prod:25            | 1       | 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:1c0b42f4e43eec4d3a18efc510c6aa0e9acd8d32 |

**fip repo:** 1c0b42f — WI823: Update excel-addin dist (Table Object Awareness)  
**Bundle (pre-deploy):** taskpane-B86y2bsw.js  
**fait-for-excel commit:** ed195f7 — WI824: FfE S8 Named Range Registration

---

## Rollback Plan

```bash
# fred-dev → fred-dev:118
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:118 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# fait-prod → fait-prod:25
aws ecs update-service --cluster fortress-tools-cluster --service fait-prod \
  --task-definition fait-prod:25 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Restore wwwroot
cd ~/projects/fip
git checkout HEAD~1 -- fait/src/FortressAI.Web/wwwroot/excel-addin/
git commit -m "rollback: restore excel-addin to pre-WI824"
git push origin main
# Then trigger CodeBuild
```

---

## Step Table

| Step | Action                          | Status | Notes |
|------|---------------------------------|--------|-------|
| 0    | Pre-deploy snapshot             | ✅ DONE | fred-dev:118, fait-prod:25, fip@1c0b42f, bundle B86y2bsw |
| 0    | Rollback plan documented        | ✅ DONE | See above |
| 1    | ADO: DEPLOY STARTING            | ✅ DONE | Comment ID 723911 |
| 2    | Build dist/                     | ✅ DONE | New bundle: taskpane-DRMs6tO9.js |
| 3    | Copy to fip wwwroot             | ✅ DONE | manifest.xml intact |
| 4    | Commit + push fip               | ✅ DONE | fip commit d3f2a5c |
| 5    | CodeBuild                       | ✅ DONE | SUCCEEDED — build id fip-fait-build:9a2f5a45-ed05-451c-a559-b05e85bf6b1b |
| 6a   | fred-dev ECS rollout            | ✅ DONE | fred-dev:118 (kb-latest image updated) — COMPLETED |
| 6b   | Register fait-prod:26           | ✅ DONE | Image: fred-chat:d3f2a5cbdf6457f0292c1550f89a0e52d7cbdce2 |
| 6c   | fait-prod ECS update to :26     | ✅ DONE | fait-prod:26 — COMPLETED |
| 7    | Health checks — fred-dev        | ✅ DONE | All 200s, bundle DRMs6tO9 confirmed |
| 8    | Health checks — fait-prod       | ✅ DONE | All 200s, bundle DRMs6tO9 confirmed |
| 9    | ADO: DEPLOY COMPLETE            | ✅ DONE | Comment ID 723912 |

---

## Health Check Results

### fred-dev (https://fait.dev.fortressam.ai)

| Endpoint                                    | Status |
|---------------------------------------------|--------|
| /health                                     | ✅ 200  |
| /excel-addin/src/taskpane/index.html        | ✅ 200  |
| /_content/FipShared/css/fip-tokens.css      | ✅ 200  |
| Bundle: taskpane-DRMs6tO9.js               | ✅ NEW  |
| manifest.xml SourceLocation                 | ✅ OK   |

### fait-prod (https://fait.fortressam.ai)

| Endpoint                                    | Status |
|---------------------------------------------|--------|
| /health                                     | ✅ 200  |
| /excel-addin/src/taskpane/index.html        | ✅ 200  |
| /_content/FipShared/css/fip-tokens.css      | ✅ 200  |
| Bundle: taskpane-DRMs6tO9.js               | ✅ NEW  |

---

## Post-Deploy Summary

| Item                  | Value |
|-----------------------|-------|
| New bundle hash       | `DRMs6tO9` |
| fip commit            | `d3f2a5c` |
| fait-for-excel commit | `ed195f7` |
| fred-dev task def     | fred-dev:118 (image updated to d3f2a5c tag) |
| fait-prod task def    | fait-prod:26 |
| CodeBuild ID          | fip-fait-build:9a2f5a45-ed05-451c-a559-b05e85bf6b1b |

---

## Verdict

✅ **DEPLOYED** — WI824 Named Range Registration is live on fred-dev and fait-prod.  
Both services healthy. fip-tokens.css 200 on both. New bundle confirmed.  
Natasha up next for QA verification.
