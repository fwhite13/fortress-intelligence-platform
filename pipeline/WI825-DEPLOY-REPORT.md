# WI825 Deploy Report — FfE S9: Reactive Workbook Watching

**Date:** 2026-03-17  
**Deployer:** War Machine (James Rhodes) — `devops`  
**Targets:** fred-dev + fait-prod  
**Status:** ✅ DEPLOYED

---

## Pre-Deploy Snapshot

| Service    | Task Definition         | Running | Image                                                                         |
|------------|------------------------|---------|-------------------------------------------------------------------------------|
| fred-dev   | fred-dev:118            | 1       | 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:kb-latest             |
| fait-prod  | fait-prod:26            | 1       | 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:d3f2a5cbdf6457f0292c1550f89a0e52d7cbdce2 |

**fip repo pre-deploy:** `d3f2a5c` — WI824: Update excel-addin dist (Named Range Registration)  
**Bundle (pre-deploy):** `taskpane-DRMs6tO9.js`  
**fait-for-excel commit:** `588fa6c` — feat(S9): WI825 reactive workbook watching — onChanged event, loop prevention, watch config UI

---

## Rollback Plan

```bash
# fred-dev → fred-dev:118
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:118 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# fait-prod → fait-prod:26
aws ecs update-service --cluster fortress-tools-cluster --service fait-prod \
  --task-definition fait-prod:26 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Restore wwwroot
cd ~/projects/fip
git checkout HEAD~1 -- fait/src/FortressAI.Web/wwwroot/excel-addin/
git commit -m "rollback: restore excel-addin to pre-WI825"
git push origin main
# Then trigger CodeBuild: fip-fait-build project
```

---

## Step Table

| Step | Action                          | Status   | Notes |
|------|---------------------------------|----------|-------|
| 0    | Pre-deploy snapshot             | ✅ DONE  | fred-dev:118, fait-prod:26, fip@d3f2a5c, bundle DRMs6tO9 |
| 0    | Rollback plan documented        | ✅ DONE  | See above |
| 1    | ADO: DEPLOY STARTING            | ✅ DONE  | Comment ID 723916 |
| 2    | Confirm fait-for-excel commit   | ✅ DONE  | `588fa6c` confirmed |
| 2    | Build dist/                     | ✅ DONE  | New bundle: `taskpane-EkUBIBFc.js`; ExcelApi 1.13 in manifest |
| 3    | Copy to fip wwwroot             | ✅ DONE  | New bundle in place; manifest ExcelApi 1.13 + SourceLocation verified |
| 4    | Commit + push fip               | ✅ DONE  | fip commit `c7943b2` |
| 5    | CodeBuild                       | ✅ DONE  | SUCCEEDED — `fip-fait-build:74997750-02e7-4e6b-9d7d-97ca6cfb68b4` |
| 6a   | fred-dev ECS force-deploy       | ✅ DONE  | fred-dev:118 (kb-latest refreshed) — STABLE |
| 6b   | Register fait-prod:27           | ✅ DONE  | Image: fred-chat:c7943b2822a4b355cce31a0f5abefc36e86cf55e |
| 6c   | fait-prod ECS update to :27     | ✅ DONE  | fait-prod:27 — STABLE |
| 7    | Health checks — fred-dev        | ✅ DONE  | All 200s, bundle EkUBIBFc confirmed, ExcelApi 1.13 in live manifest |
| 8    | Health checks — fait-prod       | ✅ DONE  | All 200s, bundle EkUBIBFc confirmed, ExcelApi 1.13 in live manifest |
| 9    | ADO: DEPLOY COMPLETE            | ✅ DONE  | Comment ID 723917 |

---

## Health Check Results

### fred-dev (https://fait.dev.fortressam.ai)

| Endpoint                                         | Status     |
|--------------------------------------------------|------------|
| /health                                          | ✅ 200     |
| /excel-addin/src/taskpane/index.html             | ✅ 200     |
| /_content/FipShared/css/fip-tokens.css           | ✅ 200     |
| /excel-addin/assets/taskpane-EkUBIBFc.js         | ✅ 200     |
| manifest.xml ExcelApi MinVersion                 | ✅ 1.13    |
| manifest.xml SourceLocation                      | ✅ fait.dev.fortressam.ai |

### fait-prod (https://fait.fortressam.ai)

| Endpoint                                         | Status     |
|--------------------------------------------------|------------|
| /health                                          | ✅ 200     |
| /excel-addin/src/taskpane/index.html             | ✅ 200     |
| /_content/FipShared/css/fip-tokens.css           | ✅ 200     |
| /excel-addin/assets/taskpane-EkUBIBFc.js         | ✅ 200     |
| manifest.xml ExcelApi MinVersion                 | ✅ 1.13    |

---

## Post-Deploy Summary

| Item                    | Value |
|-------------------------|-------|
| New bundle hash         | `EkUBIBFc` |
| Previous bundle hash    | `DRMs6tO9` |
| fip commit (post)       | `c7943b2` |
| fip commit (pre)        | `d3f2a5c` |
| fait-for-excel commit   | `588fa6c` |
| fred-dev task def       | fred-dev:118 (kb-latest refreshed) |
| fait-prod task def      | fait-prod:27 |
| CodeBuild ID            | fip-fait-build:74997750-02e7-4e6b-9d7d-97ca6cfb68b4 |
| ExcelApi requirement    | 1.13 (confirmed in both live manifests) |

---

## Critical Lessons Applied

| Lesson | Applied |
|--------|---------|
| wwwroot baked into Docker — CodeBuild rebuild required | ✅ CodeBuild triggered and SUCCEEDED |
| fip repo must be pushed before CodeBuild | ✅ Pushed c7943b2 before triggering build |
| fait-prod static tag — register fait-prod:27 | ✅ fait-prod:27 registered and deployed |
| fip-tokens.css 200 mandatory | ✅ 200 on both envs |
| Verify ExcelApi 1.13 survives copy to wwwroot | ✅ Confirmed pre-copy (dist), post-copy (fip), and in live manifests |

---

## Verdict

**✅ DEPLOYED — All systems green. Natasha to verify.**

WI825 Reactive Workbook Watching is live on fred-dev and fait-prod. Both environments serving new bundle `EkUBIBFc` with ExcelApi 1.13 requirement declared in manifest. fip-tokens.css 200 on both. No issues encountered.
