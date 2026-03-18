# WI826 Deploy Report — FfE S10: Multi-Sheet Report Generation

**Date:** 2026-03-17  
**Deployer:** War Machine (James Rhodes) — `devops`  
**Targets:** fred-dev + fait-prod  
**Status:** ✅ DEPLOYED

---

## Pre-Deploy Snapshot

| Service    | Task Definition  | Running | Bundle                     |
|------------|-----------------|---------|----------------------------|
| fred-dev   | fred-dev:118    | 1       | taskpane-EkUBIBFc.js (WI825) |
| fait-prod  | fait-prod:27    | 1       | taskpane-EkUBIBFc.js (WI825) |

**fip repo pre-deploy:** `c7943b2` — WI825: Update excel-addin dist (Reactive Workbook Watching, ExcelApi 1.13)  
**Bundle (pre-deploy):** `taskpane-EkUBIBFc.js`  
**fait-for-excel commit (pre-deploy):** `588fa6c` — feat(S9): WI825 reactive workbook watching  
**fait-for-excel HEAD (to deploy):** `c1093f8` — WI826: Remove setFaitWriting guard from reportBuilder.ts

---

## Rollback Plan

```bash
# fred-dev → fred-dev:118
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:118 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# fait-prod → fait-prod:27
aws ecs update-service --cluster fortress-tools-cluster --service fait-prod \
  --task-definition fait-prod:27 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Restore wwwroot
cd ~/projects/fip
git checkout HEAD~1 -- fait/src/FortressAI.Web/wwwroot/excel-addin/
git commit -m "rollback: restore excel-addin to pre-WI826"
git push origin main
# Then trigger CodeBuild: fip-fait-build project
```

---

## Step Table

| Step | Action                          | Status   | Notes |
|------|---------------------------------|----------|-------|
| 0    | Pre-deploy snapshot             | ✅ DONE  | fred-dev:118, fait-prod:27, fip@c7943b2, bundle EkUBIBFc |
| 0    | Rollback plan documented        | ✅ DONE  | See above |
| 1    | ADO: DEPLOY STARTING            | ✅ DONE  | Comment ID 723925 |
| 2    | Confirm fait-for-excel commit   | ✅ DONE  | `c1093f8` confirmed HEAD |
| 2    | Build dist/                     | ✅ DONE  | New bundle: `taskpane-Bu81Do3I.js` (57 modules, 290KB) |
| 3    | Copy to fip wwwroot             | ✅ DONE  | Old bundle removed, new bundle in place; manifest ExcelApi 1.13 verified |
| 4    | Commit + push fip               | ✅ DONE  | fip commit `64c8353` — pushed to main |
| 5    | CodeBuild: fip-fait-build       | ✅ DONE  | SUCCEEDED — `fip-fait-build:72ca2f7a-4031-4c22-962b-a1cd865022e9` |
| 6a   | fred-dev ECS force-deploy       | ✅ DONE  | fred-dev:118 (kb-latest refreshed) — STABLE |
| 6b   | Register fait-prod:28           | ✅ DONE  | Image: fred-chat:64c83538044f7c14cfbfbf85e0b18383987de4b1 |
| 6c   | fait-prod ECS update to :28     | ✅ DONE  | fait-prod:28 — STABLE |
| 7    | Health checks — fred-dev        | ✅ DONE  | All 200s, bundle Bu81Do3I confirmed |
| 8    | Health checks — fait-prod       | ✅ DONE  | All 200s, bundle Bu81Do3I confirmed |
| 9    | ADO: DEPLOY COMPLETE            | ✅ DONE  | Comment ID 723926 |

---

## Health Check Results

### fred-dev (https://fait.dev.fortressam.ai)

| Endpoint                                         | Status     |
|--------------------------------------------------|------------|
| /health                                          | ✅ 200     |
| /_content/FipShared/css/fip-tokens.css           | ✅ 200     |
| Bundle: taskpane-Bu81Do3I.js                     | ✅ LIVE    |

### fait-prod (https://fait.fortressam.ai)

| Endpoint                                         | Status     |
|--------------------------------------------------|------------|
| /health                                          | ✅ 200     |
| /_content/FipShared/css/fip-tokens.css           | ✅ 200     |
| Bundle: taskpane-Bu81Do3I.js                     | ✅ LIVE    |

---

## Post-Deploy Summary

| Item                    | Value |
|-------------------------|-------|
| New bundle hash         | `Bu81Do3I` |
| Previous bundle hash    | `EkUBIBFc` |
| fip commit (post)       | `64c8353` |
| fip commit (pre)        | `c7943b2` |
| fait-for-excel commit   | `c1093f8` |
| fred-dev task def       | `fred-dev:118` (same revision, new image via kb-latest) |
| fait-prod task def      | `fait-prod:28` (new revision, image 64c83538...) |
| CodeBuild build ID      | `fip-fait-build:72ca2f7a-4031-4c22-962b-a1cd865022e9` |
| ECR image digest        | `sha256:c11f5e2439e4d29c616c03ac04ecc4fd1f366c9456fb96ceb9ce84760730dd8a` |

---

## What Shipped

**WI826 — FfE S10: Multi-Sheet Report Generation**

- `/report` command — triggers two-phase flow (FAIT analysis → "Create Report Sheet" button)
- `report_spec` parser — parses structured report specification from FAIT response  
- `createReportSheet()` — creates new Excel sheet with title, summary, metrics table, and chart
- `reportSpec` on `Message` — carries report spec through the message lifecycle
- `setFaitWriting` guard removed from `reportBuilder.ts` (library should not own guard — fix in c1093f8)

---

## Verdict

✅ **DEPLOY SUCCESSFUL** — Both environments healthy. Bundle `Bu81Do3I` live. fip-tokens.css 200/200.  
Awaiting Natasha's verification.
