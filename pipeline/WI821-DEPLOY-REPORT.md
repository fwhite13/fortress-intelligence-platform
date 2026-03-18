# Deploy Report: WI821

## Pre-Deploy Snapshot
- fred-dev ECS: `fred-dev:118` (1/1 running, image: `fred-chat:kb-latest`)
- fait-prod ECS: `fait-prod:23` (1/1 running, image: `fred-chat:6123332`)
- fip repo HEAD before: `ca6f17b` — WI814: Update excel-addin dist (writeRangeData, empty selection indicator, dimension mismatch errors)
- Bundle hash before: `DtS61AUh`

## Rollback Plan
```bash
# Roll ECS fred-dev back to fred-dev:118
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:118 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer

# Roll ECS fait-prod back to fait-prod:23
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-prod \
  --task-definition fait-prod:23 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer

# Restore excel-addin in fip repo
cd ~/projects/fip
git revert 69b84ee --no-edit
git push origin main
# Then trigger CodeBuild to rebuild
```

## Steps Completed
| Step | Status | Notes |
|------|--------|-------|
| ADO STARTING comment | ✅ | Comment ID 723888, posted 2026-03-17T02:32:34Z |
| dist/ built from fe70ff2 | ✅ | New hash: `CdqFJY08` (was `DtS61AUh`) |
| manifest.xml URLs in wwwroot verified | ✅ | Both SourceLocation URLs point to `.../src/taskpane/index.html` |
| fip committed + pushed | ✅ | Commit `69b84ee` |
| CodeBuild SUCCEEDED | ✅ | `fip-fait-build:8300e70b-0968-4d24-b4b1-9569a7bf303f` |
| fred-dev ECS updated | ✅ | rolloutState=COMPLETED (kb-latest tag, new digest) |
| fait-prod ECS updated | ✅ | rolloutState=COMPLETED (fait-prod:24, image `69b84ee...`) |
| fred-dev health checks all 200 | ✅ | All 3 endpoints 200 |
| fait-prod health checks all 200 | ✅ | All 3 endpoints 200 |
| ADO COMPLETE comment | ✅ | Comment ID 723890, posted 2026-03-17T02:42:00Z |

## Health Check Results (fred-dev)
- /health: 200
- /excel-addin/src/taskpane/index.html: 200
- fip-tokens.css: 200
- New bundle hash: `taskpane-CdqFJY08.js`
- manifest.xml SourceLocation: `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html`

## Health Check Results (fait-prod)
- /health: 200
- /excel-addin/src/taskpane/index.html: 200
- fip-tokens.css: 200
- New bundle hash: `taskpane-CdqFJY08.js`

## Deployment Details
- fip commit: `69b84ee` (WI821: Update excel-addin dist — table parser, TableRenderer, Write to Sheet flow)
- fred-dev task def: `fred-dev:118` (force-new-deployment with updated `kb-latest` image)
- fait-prod task def: `fait-prod:24` (new task def pointing to full commit hash image)
- CodeBuild ID: `fip-fait-build:8300e70b-0968-4d24-b4b1-9569a7bf303f`
- Deploy time: 22:32 EDT → 22:42 EDT (~10 minutes)

## Notes
- fait-prod required a new ECS task definition (`fait-prod:24`) because its task def referenced image tag `6123332` (static), while CodeBuild pushes to `kb-latest` and full commit hash tags. Created `fait-prod:24` pointing to the new `69b84ee...` full commit hash image tag and force-deployed.
- Both services confirmed serving `CdqFJY08` bundle hash post-deploy.

## Verdict: DEPLOYED ✅
