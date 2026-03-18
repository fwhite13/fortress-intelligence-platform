# WI828 Deploy Report — FfP Sprint 1: Foundation + Core Chat + Apply to Shape

**Agent:** War Machine (James Rhodes)  
**Date:** 2026-03-17  
**Deploy Start:** 02:29 EDT  
**Deploy Complete:** 02:37 EDT  
**Total Time:** ~8 minutes  

---

## Pre-Deploy Snapshot

| Service   | Task Definition | Running | fip Commit |
|-----------|----------------|---------|------------|
| fred-dev  | fred-dev:118   | 1       | 8304af3    |
| fait-prod | fait-prod:29   | 1       | 8304af3    |

**ppt-addin state:** Not yet created (`~/projects/fip/fait/src/FortressAI.Web/wwwroot/ppt-addin/` did not exist)  
**Snapshot timestamp:** 02:29 EDT

---

## Rollback Plan

Execute if health checks fail or Natasha reports FAIL verdict:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer

# fred-dev → fred-dev:118
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:118 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# fait-prod → fait-prod:29
aws ecs update-service --cluster fortress-tools-cluster --service fait-prod \
  --task-definition fait-prod:29 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Revert fip repo
cd ~/projects/fip
git checkout HEAD~1 -- fait/src/FortressAI.Web/wwwroot/ppt-addin/
git commit -m "rollback: remove ppt-addin from wwwroot (pre-WI828)"
git push origin main
# Then trigger CodeBuild: fip-fait-build
```

---

## Step Table

| Step | Action                                      | Status   | Notes |
|------|---------------------------------------------|----------|-------|
| 0    | Pre-deploy snapshot                         | ✅ DONE  | fred-dev:118, fait-prod:29, fip@8304af3 |
| 0    | Rollback plan documented                    | ✅ DONE  | See above |
| 1    | ADO: DEPLOY STARTING                        | ✅ DONE  | Comment ID 723946 |
| 2    | Verify fip HEAD = 240c3b3                   | ✅ DONE  | Confirmed present, pushed to remote |
| 2    | Check FAIT Dockerfile                       | ✅ DONE  | Copies entire `fait/src/` — no COPY change needed |
| 3    | Build FfP dist/                             | ✅ DONE  | tsc + vite build — 38 modules, taskpane.js 221KB, 92ms |
| 4    | Create wwwroot/ppt-addin/ + copy dist       | ✅ DONE  | 9 files: manifest.xml, src/taskpane/index.html, assets/* |
| 4    | Verify manifest                             | ✅ DONE  | Host Name="Presentation", SourceLocation=/ppt-addin/src/taskpane/index.html |
| 5    | Dockerfile check                            | ✅ DONE  | No change needed — COPY fait/src/ covers wwwroot |
| 6    | Commit + push fip                           | ✅ DONE  | fip commit `8137304` — pushed to main |
| 7    | CodeBuild: fip-fait-build                   | ✅ DONE  | SUCCEEDED — build #160, `ff9730f2-dd28-4cc0-bbf1-780e13297d96` |
| 8a   | fred-dev ECS force-deploy                   | ✅ DONE  | fred-dev:118 (kb-latest refreshed) — STABLE |
| 8b   | Register fait-prod:30                       | ✅ DONE  | Image: fred-chat:8137304d8e17bfa01ba8df203fa928fe77f6a600 |
| 8c   | fait-prod ECS update to :30                 | ✅ DONE  | fait-prod:30 — STABLE |
| 9    | Health checks — fred-dev                    | ✅ DONE  | All 200s incl. ppt-addin |
| 9    | Health checks — fait-prod                   | ✅ DONE  | All 200s incl. ppt-addin |
| 10   | ADO: DEPLOY COMPLETE                        | ✅ DONE  | Comment ID 723950 |

---

## Health Check Results

### fred-dev (https://fait.dev.fortressam.ai)

| Endpoint                                              | Status  |
|-------------------------------------------------------|---------|
| /health                                               | ✅ 200  |
| /_content/FipShared/css/fip-tokens.css                | ✅ 200  |
| /excel-addin/src/taskpane/index.html                  | ✅ 200  |
| /ppt-addin/src/taskpane/index.html ⭐ NEW             | ✅ 200  |

### fait-prod (https://fait.fortressam.ai)

| Endpoint                                              | Status  |
|-------------------------------------------------------|---------|
| /health                                               | ✅ 200  |
| /_content/FipShared/css/fip-tokens.css                | ✅ 200  |
| /excel-addin/src/taskpane/index.html                  | ✅ 200  |
| /ppt-addin/src/taskpane/index.html ⭐ NEW             | ✅ 200  |

---

## Deployment Summary

| Field                   | Value |
|-------------------------|-------|
| WI                      | WI828 |
| fip commit (pre-deploy) | 8304af3 |
| fip commit (deployed)   | 8137304 |
| ECR image digest        | sha256:549788e19edb364017247b201965a7b3b6167709634d7f13951f75a72538fc49 |
| ECR image tag           | 8137304d8e17bfa01ba8df203fa928fe77f6a600 |
| CodeBuild build ID      | fip-fait-build:ff9730f2-dd28-4cc0-bbf1-780e13297d96 (build #160) |
| fred-dev task def       | fred-dev:118 (kb-latest refreshed) |
| fait-prod task def      | fait-prod:30 |
| Dockerfile change       | None — COPY fait/src/ already covers wwwroot/ppt-addin/ |
| ppt-addin files         | 9 files (manifest.xml, src/taskpane/index.html, assets/taskpane.js, assets/taskpane.css, assets/icon-*.png, commands.html, public/commands.html) |

---

## Notes

- **Dockerfile**: No change required. The existing `COPY fait/src/ fait/src/` instruction picks up all wwwroot content including the new ppt-addin directory.
- **fip push**: Two commits pushed in sequence — 240c3b3 (FfP Sprint 1 source code) already in remote before this deploy; 8137304 (wwwroot ppt-addin addition) pushed as part of Step 6.
- **Manifest**: Verified `Host Name="Presentation"` and SourceLocation pointing to `/ppt-addin/src/taskpane/index.html`. Icons at `fait.dev.fortressam.ai/ppt-addin/assets/`.
- **taskpane.js**: No hash suffix (221KB, Sprint 1 convention), as expected per deploy instructions.

---

## Verdict

**✅ DEPLOY SUCCESS**

WI828 FfP Sprint 1 fully deployed to fred-dev and fait-prod. ppt-addin endpoint live on both environments. All 8 health checks green including the new `/ppt-addin/src/taskpane/index.html` endpoint and mandatory `fip-tokens.css`. fait-prod registered as task def :30.

**Awaiting Natasha (Black Widow) QA verification.**
