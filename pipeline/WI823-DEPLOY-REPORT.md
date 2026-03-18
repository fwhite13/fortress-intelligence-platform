# WI823 Deploy Report — FfE S7: Table Object Awareness

**Agent:** War Machine (James Rhodes / `devops`)
**Date:** 2026-03-16 / 2026-03-17 (EDT)
**Status:** ✅ DEPLOY COMPLETE

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| fred-dev task def | `fred-dev:118` |
| fait-prod task def | `fait-prod:24` |
| fip commit (pre) | `69b84ee` — WI821: Update excel-addin dist |
| Old bundle hash | `CdqFJY08` (taskpane-CdqFJY08.js) |
| fred-dev running | 1 task |
| fait-prod running | 1 task |

---

## Rollback Plan (documented before Step 1)

```bash
# Roll fred-dev back to fred-dev:118
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:118 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Roll fait-prod back to fait-prod:24
aws ecs update-service --cluster fortress-tools-cluster --service fait-prod \
  --task-definition fait-prod:24 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Restore wwwroot
cd ~/projects/fip
git checkout HEAD~1 -- fait/src/FortressAI.Web/wwwroot/excel-addin/
git commit -m "rollback: restore excel-addin to pre-WI823"
git push origin main
# Then trigger CodeBuild
```

---

## Step Execution Table

| Step | Action | Result | Details |
|------|--------|--------|---------|
| Pre-snapshot | ECS + fip state | ✅ | fred-dev:118, fait-prod:24, fip@69b84ee, bundle CdqFJY08 |
| 1 | ADO: DEPLOY STARTING | ✅ | Comment ID 723900, posted 03:45:27Z |
| 2 | `npm run build` in fait-for-excel | ✅ | Bundle `taskpane-B86y2bsw.js` (266.75 kB), 54 modules |
| 3 | Copy dist → fip wwwroot | ✅ | Manifest verified — points to `/src/taskpane/index.html` |
| 4 | git commit + push fip | ✅ | Commit `1c0b42f` — "WI823: Update excel-addin dist (Table Object Awareness)" |
| 5 | CodeBuild `fip-fait-build` | ✅ SUCCEEDED | Build ID: `dcc839e5-8f91-4857-9e98-8d69e79ca38a`, ~2 min |
| 6a | ECS fred-dev rollout | ✅ COMPLETED | Picked up new ECR image automatically via fred-dev:118 |
| 6b | fait-prod:25 registration | ✅ | New task def registered with image `1c0b42f4...` |
| 6c | ECS fait-prod update | ✅ COMPLETED | Updated to fait-prod:25, rollout COMPLETED |
| 7 | Health checks: fred-dev | ✅ ALL 200 | See health check table below |
| 8 | Health checks: fait-prod | ✅ ALL 200 | See health check table below |
| 9 | ADO: DEPLOY COMPLETE | ✅ | Comment ID 723902, posted 03:53:05Z |

---

## Health Check Results

### fred-dev (https://fait.dev.fortressam.ai)

| Endpoint | HTTP | Notes |
|----------|------|-------|
| `/health` | **200** | ✅ |
| `/excel-addin/src/taskpane/index.html` | **200** | ✅ |
| `/_content/FipShared/css/fip-tokens.css` | **200** | ✅ |
| Bundle hash | **`B86y2bsw`** | ✅ New hash confirms fresh code |
| Manifest SourceLocation | `/excel-addin/src/taskpane/index.html` | ✅ |

### fait-prod (https://fait.fortressam.ai)

| Endpoint | HTTP | Notes |
|----------|------|-------|
| `/health` | **200** | ✅ |
| `/excel-addin/src/taskpane/index.html` | **200** | ✅ |
| `/_content/FipShared/css/fip-tokens.css` | **200** | ✅ |
| Bundle hash | **`B86y2bsw`** | ✅ New hash confirms fresh code |

---

## Deployment Details

| Item | Value |
|------|-------|
| fait-for-excel commits deployed | `65068b2` + `f1b537e` + `d35c3f5` |
| New bundle | `taskpane-B86y2bsw.js` |
| fip commit (post) | `1c0b42f` |
| ECR image tag | `1c0b42f4e43eec4d3a18efc510c6aa0e9acd8d32` |
| fred-dev task def | `fred-dev:118` (unchanged — CodeBuild updates ECR, ECS picks up new image) |
| fait-prod task def | `fait-prod:25` (new revision pointing to `1c0b42f4...` image) |
| CodeBuild ID | `fip-fait-build:dcc839e5-8f91-4857-9e98-8d69e79ca38a` |
| Build duration | ~2 minutes |
| Total deploy time | ~10 minutes |

---

## What Was Deployed

**WI823 — Sprint 7: Table Object Awareness**

- `ExcelReader` detects Excel Tables and exposes table metadata
- `contextFormatter` uses Table column names for richer context
- `writeToTable()` appends rows to detected Tables
- Green Table badge in `ContextIndicator` UI
- Routing regex fix (constrain column to 1–3 letters, prevent SalesData2023 false positive)
- Empty Table fix (`getDataBodyRangeOrNullObject` — handle Tables with no data rows)

---

## Verdict

✅ **DEPLOY SUCCESSFUL**

Both fred-dev and fait-prod are healthy. All health checks pass. fip-tokens.css returns 200 on both environments. New bundle hash `B86y2bsw` confirms WI823 code is live. Natasha to verify.

---

*War Machine out.*
