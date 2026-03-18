# WI827 Deploy Report — FfE S11: Formula Intelligence

**Date:** 2026-03-17  
**Deployer:** War Machine (James Rhodes) — `devops`  
**Targets:** fred-dev + fait-prod  
**Status:** ✅ DEPLOYED

---

## Pre-Deploy Snapshot

| Service    | Task Definition  | Running | Bundle                     |
|------------|-----------------|---------|----------------------------|
| fred-dev   | fred-dev:118    | 1       | taskpane-Bu81Do3I.js (WI826) |
| fait-prod  | fait-prod:28    | 1       | taskpane-Bu81Do3I.js (WI826) |

**fip repo pre-deploy:** `64c8353` — WI826: Update excel-addin dist (/report command, report_spec, createReportSheet)  
**Bundle (pre-deploy):** `taskpane-Bu81Do3I.js`  
**fait-for-excel HEAD (to deploy):** `0671ddc` — WI827 C2: Fix comments.add() sync boundary; VeryHidden → SheetVisibility enum

---

## Rollback Plan

```bash
# fred-dev → fred-dev:118
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:118 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# fait-prod → fait-prod:28
aws ecs update-service --cluster fortress-tools-cluster --service fait-prod \
  --task-definition fait-prod:28 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Restore wwwroot
cd ~/projects/fip
git checkout HEAD~1 -- fait/src/FortressAI.Web/wwwroot/excel-addin/
git commit -m "rollback: restore excel-addin to pre-WI827"
git push origin main
# Then trigger CodeBuild: fip-fait-build project
```

---

## Step Table

| Step | Action                          | Status   | Notes |
|------|---------------------------------|----------|-------|
| 0    | Pre-deploy snapshot             | ✅ DONE  | fred-dev:118, fait-prod:28, fip@64c8353, bundle Bu81Do3I |
| 0    | Rollback plan documented        | ✅ DONE  | See above |
| 1    | ADO: DEPLOY STARTING            | ✅ DONE  | Comment ID 723935 |
| 2    | Confirm fait-for-excel commit   | ✅ DONE  | `0671ddc` confirmed HEAD |
| 2    | Build dist/                     | ✅ DONE  | New bundle: `taskpane-0jKgr1fV.js` (58 modules, 298KB, 109ms) |
| 3    | Copy to fip wwwroot             | ✅ DONE  | Old bundle removed, new bundle in place; manifest ExcelApi 1.13 verified |
| 4    | Commit + push fip               | ✅ DONE  | fip commit `8304af3` — pushed to main |
| 5    | CodeBuild: fip-fait-build       | ✅ DONE  | SUCCEEDED — `fip-fait-build:04b1d0f2-c306-4e65-ba4d-f3dd3ed76a5a` (build #159) |
| 6a   | fred-dev ECS force-deploy       | ✅ DONE  | fred-dev:118 (kb-latest refreshed) — STABLE |
| 6b   | Register fait-prod:29           | ✅ DONE  | Image: fred-chat:8304af389ec8966fa4b74cae6b4044dcc5bad74c |
| 6c   | fait-prod ECS update to :29     | ✅ DONE  | fait-prod:29 — STABLE |
| 7    | Health checks — fred-dev        | ✅ DONE  | All 200s, bundle 0jKgr1fV confirmed |
| 8    | Health checks — fait-prod       | ✅ DONE  | All 200s, bundle 0jKgr1fV confirmed |
| 9    | ADO: DEPLOY COMPLETE            | ✅ DONE  | Comment ID 723936 |

---

## Health Check Results

### fred-dev (https://fait.dev.fortressam.ai)

| Endpoint                                         | Status     |
|--------------------------------------------------|------------|
| /health                                          | ✅ 200     |
| /_content/FipShared/css/fip-tokens.css           | ✅ 200     |
| Bundle: taskpane-0jKgr1fV.js                     | ✅ LIVE    |

### fait-prod (https://fait.fortressam.ai)

| Endpoint                                         | Status     |
|--------------------------------------------------|------------|
| /health                                          | ✅ 200     |
| /_content/FipShared/css/fip-tokens.css           | ✅ 200     |
| Bundle: taskpane-0jKgr1fV.js                     | ✅ LIVE    |

---

## Post-Deploy Summary

| Item                    | Value |
|-------------------------|-------|
| New bundle hash         | `0jKgr1fV` |
| Previous bundle hash    | `Bu81Do3I` |
| fip commit (post)       | `8304af3` |
| fip commit (pre)        | `64c8353` |
| fait-for-excel commit   | `0671ddc` |
| fred-dev task def       | `fred-dev:118` (same revision, new image via kb-latest) |
| fait-prod task def      | `fait-prod:29` (new revision, image 8304af38...) |
| CodeBuild build ID      | `fip-fait-build:04b1d0f2-c306-4e65-ba4d-f3dd3ed76a5a` |
| ECR image digest        | `sha256:6aade82190aeed2e70def641417d86ac9bc2320ec8700a5dc7021fc7f08fbf31` |

---

## What Shipped

**WI827 — FfE S11: Formula Intelligence**

- `/formula` command — triggers formula suggestion flow
- `formula_spec` parser — parses structured formula specification from FAIT response
- `formulaBuilder.ts` — builds formula with scratch-cell preview using `__FAIT_SCRATCH__` veryHidden sheet (SheetVisibility enum, not string literal)
- `writeFormula()` — writes formula to target cell with `setFaitWriting` guard
- `formulaSpec` on `Message` — carries formula spec through the message lifecycle
- `comments.add()` sync boundary fix — resolved in review cycle 2 (C2)

---

## Verdict

✅ **DEPLOY SUCCESSFUL** — Both environments healthy. Bundle `0jKgr1fV` live. fip-tokens.css 200/200.  
Awaiting Natasha's verification.
