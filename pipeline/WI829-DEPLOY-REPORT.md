# WI829 Deploy Report — FfP Sprint 2: Full Slide Scan + FORGE Search + /notes + Source Tagging

**Agent:** War Machine (James Rhodes)  
**Date:** 2026-03-17  
**Deploy Start:** 03:01 EDT  
**Deploy Complete:** 03:19 EDT  
**Total Time:** ~18 minutes  

---

## Pre-Deploy Snapshot

| Service   | Task Definition | Running | fip Commit     |
|-----------|----------------|---------|----------------|
| fred-dev  | fred-dev:118   | 1       | 8137304 (WI828) |
| fait-prod | fait-prod:30   | 1       | 8137304 (WI828) |

**ppt-addin state (pre-deploy):** `taskpane.js` (Sprint 1, no hash suffix)  
**excel-addin bundle:** `taskpane-0jKgr1fV.js` (must remain unchanged)  
**Snapshot timestamp:** 03:01 EDT

---

## Rollback Plan

Execute if health checks fail or Natasha reports FAIL verdict:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer

# fred-dev → fred-dev:118 (will pick up kb-latest from CodeBuild — rollback requires git revert + rebuild)
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:118 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# fait-prod → fait-prod:30
aws ecs update-service --cluster fortress-tools-cluster --service fait-prod \
  --task-definition fait-prod:30 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

cd ~/projects/fip
git checkout HEAD~1 -- fait/src/FortressAI.Web/wwwroot/ppt-addin/
git commit -m "rollback: restore ppt-addin to pre-WI829"
git push origin main
# Then trigger CodeBuild: fip-fait-build
```

---

## Step Table

| Step | Action                                        | Status   | Notes |
|------|-----------------------------------------------|----------|-------|
| 0    | Pre-deploy snapshot                           | ✅ DONE  | fred-dev:118, fait-prod:30, fip@8137304 |
| 0    | Rollback plan documented                      | ✅ DONE  | See above |
| 1    | ADO: DEPLOY STARTING                          | ✅ DONE  | Comment ID 723956 |
| 2    | Verify fip HEAD = d4af147                     | ✅ DONE  | Confirmed: `d4af147 WI829: FfP Sprint 2 — full slide scan, FORGE panel, /notes command, source tagging` |
| 3    | npm run build (fait-for-powerpoint)           | ✅ DONE  | tsc + vite build — 41 modules, taskpane.js 232KB, 99ms |
| 4    | Copy dist → wwwroot/ppt-addin/                | ✅ DONE  | 7 files: manifest.xml, src/taskpane/index.html, assets/taskpane.js, assets/taskpane.css, assets/icon-*.png, commands.html, public/commands.html |
| 4    | Verify manifest PowerPointApi                 | ✅ DONE  | `<Set Name="PowerPointApi" MinVersion="1.6"/>` confirmed |
| 4    | Verify excel-addin untouched                  | ✅ DONE  | `taskpane-0jKgr1fV.js` present, not modified |
| 5    | Commit + push fip                             | ✅ DONE  | fip commit `ac9c455` pushed to main |
| 6    | CodeBuild: fip-fait-build                     | ✅ DONE  | SUCCEEDED — build #161, `fip-fait-build:9dd4121a-bd25-436b-abfd-7a18a89e8581` |
| 7    | fred-dev ECS stabilize (kb-latest refreshed)  | ✅ DONE  | fred-dev:118 — STABLE (buildspec force-deploys fred-dev) |
| 8    | Register fait-prod:31                         | ✅ DONE  | Image: fred-chat:ac9c455513a2756aea2376d1c65d189365776e46 |
| 9    | fait-prod ECS update to :31                   | ✅ DONE  | fait-prod:31 — STABLE |
| 10   | Health checks — fred-dev                      | ✅ DONE  | All 200s incl. ppt-addin |
| 10   | Health checks — fait-prod                     | ✅ DONE  | All 200s incl. ppt-addin |
| 11   | ADO: DEPLOY COMPLETE                          | ✅ DONE  | Comment ID 723957 |

---

## Health Check Results

### fred-dev (https://fait.dev.fortressam.ai)

| Endpoint                                              | Status  |
|-------------------------------------------------------|---------|
| /health                                               | ✅ 200  |
| /_content/FipShared/css/fip-tokens.css                | ✅ 200  |
| /excel-addin/src/taskpane/index.html                  | ✅ 200  |
| /ppt-addin/src/taskpane/index.html                    | ✅ 200  |
| FfE bundle check (`taskpane-0jKgr1fV.js`)             | ✅ PASS — unchanged |

### fait-prod (https://fait.fortressam.ai)

| Endpoint                                              | Status  |
|-------------------------------------------------------|---------|
| /health                                               | ✅ 200  |
| /_content/FipShared/css/fip-tokens.css                | ✅ 200  |
| /excel-addin/src/taskpane/index.html                  | ✅ 200  |
| /ppt-addin/src/taskpane/index.html                    | ✅ 200  |
| FfE bundle check (`taskpane-0jKgr1fV.js`)             | ✅ PASS — unchanged |

---

## Deployment Summary

| Field                      | Value |
|----------------------------|-------|
| WI                         | WI829 |
| fip commit (pre-deploy)    | 8137304 (WI828) |
| fip commit (deployed)      | ac9c455 |
| ECR image tag              | ac9c455513a2756aea2376d1c65d189365776e46 |
| ECR image digest           | sha256:328c7974a3381224f0e1f9744b3e615dae84b6d76deacc411c998636b815b5de |
| ECR floating tag           | kb-latest (refreshed 03:13 EDT) |
| CodeBuild build ID         | fip-fait-build:9dd4121a-bd25-436b-abfd-7a18a89e8581 (build #161) |
| fred-dev task def          | fred-dev:118 (kb-latest refreshed in-place) |
| fait-prod task def         | fait-prod:31 |
| ppt-addin taskpane.js      | 232KB (no hash suffix — Sprint 2 convention) |
| PowerPointApi MinVersion   | 1.6 |
| FfE bundle                 | taskpane-0jKgr1fV.js — UNCHANGED ✅ |

---

## Notes

- **CodeBuild webhook**: GitHub push to `fortress-intelligence-platform` main does NOT auto-trigger CodeBuild (webhook likely configured for `fortress-tools-dotnet` per setup script). CodeBuild was manually triggered via `aws codebuild start-build --project-name fip-fait-build` using default env creds (not `--profile` flag — env vars work without it).
- **fait-prod:31 image**: Updated from `8137304...` (WI828) to `ac9c455...` (WI829). All prod env vars carried forward from :30.
- **ppt-addin diff**: `taskpane.js` (+11/-10 lines) and `manifest.xml` (+1/-0 — PowerPointApi 1.6 set) per git diff.
- **FfE regression**: Both environments confirmed `taskpane-0jKgr1fV.js` — excel-addin wwwroot was not touched.
- **fip-tokens.css**: 200 on both environments — mandatory check passed.

---

## Verdict

**✅ DEPLOY SUCCESS**

WI829 FfP Sprint 2 fully deployed to fred-dev and fait-prod. All health checks green. ppt-addin Sprint 2 (getAllSlidesContext, formatDeckContext, getSlideNotes, writeNotes, tagShape, /notes command, FORGE panel) live on both environments. fait-prod:31 registered and stable. FfE 0jKgr1fV bundle unchanged.

**Awaiting Natasha (Black Widow) QA verification.**
