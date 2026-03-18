# Deploy Report: WI814

## Pre-Deploy Snapshot
- **fred-dev ECS state:** fred-dev:118, ACTIVE, running 1/1, desired 1
- **Current image:** 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:kb-latest
- **excel-addin/ key files before:**
  - `assets/taskpane-t0ZrHc1u.js`
  - `commands.html`
  - `public/commands.html`
  - `src/taskpane/index.html`
- **fip repo HEAD before:** `867148d WI813: Update excel-addin manifest.xml with correct Taskpane URLs`

## Rollback Plan
```bash
# Roll ECS back to previous task def (fred-dev:118)
# Note: task def revision stays :118 since image uses floating kb-latest tag.
# To rollback content, restore fip repo and rebuild:
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:118 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer

# Restore excel-addin contents in fip repo
cd ~/projects/fip
git checkout HEAD~1 -- fait/src/FortressAI.Web/wwwroot/excel-addin/
git commit -m "rollback: restore excel-addin to pre-WI814 state"
git push origin main

# Trigger CodeBuild to rebuild with reverted source
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws codebuild start-build \
  --project-name fip-fait-build \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

## Steps Completed

| Step | Status | Notes |
|------|--------|-------|
| ADO STARTING comment | ✅ | comment id 723557, posted 17:45:02Z |
| dist/ built from 6c8649e | ✅ | `npm run build` succeeded; new bundle: `taskpane-DtS61AUh.js` |
| dist/ copied to wwwroot | ✅ | old bundle removed, new bundle + html in place |
| manifest.xml URLs verified in wwwroot | ✅ | Both show `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html` |
| fip committed + pushed | ✅ | `ca6f17b` pushed to github.com:fwhite13/fortress-intelligence-platform |
| CodeBuild triggered + succeeded | ✅ | `fip-fait-build:42d59b8d-9cf5-4054-a4b3-21bbe7f8f67a` — SUCCEEDED |
| ECS updated | ✅ | Deployment COMPLETED 13:50:02 EDT; rolloutState: COMPLETED |
| Health checks all 200 | ✅ | All three endpoints returned 200 |
| manifest.xml live URLs verified | ✅ | Both SourceLocation values correct |
| ADO COMPLETE comment | ✅ | comment id 723560, posted 17:50:50Z |

## Health Check Results
- **FAIT health:** 200
- **/excel-addin/src/taskpane/index.html:** 200
- **fip-tokens.css:** 200
- **manifest.xml SourceLocation:** `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html`
- **manifest.xml Taskpane.Url:** `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html`

## Deployment Details
- **fip commit:** `ca6f17b` — "WI814: Update excel-addin dist (writeRangeData, empty selection indicator, dimension mismatch errors)"
- **ECS task def:** fred-dev:118 (image tag `kb-latest` updated in-place by CodeBuild)
- **CodeBuild ID:** `fip-fait-build:42d59b8d-9cf5-4054-a4b3-21bbe7f8f67a`
- **Deploy time:** 13:45 – 13:51 EDT (approx. 6 minutes)

## Verdict: DEPLOYED ✅
