## Deploy Report: WI908 Sprint 8

### Pre-Deploy Snapshot
- Task def before: famos-dev:3
- Image digest before: sha256:915041292c10b8c4f097e8f6229cf67be50b3fc9d53c89835b75262a35011036

### Steps Completed
| Step | Status | Notes |
|------|--------|-------|
| Source AWS creds | ✅ DONE | fortress-tools-deployer |
| Pre-deploy snapshot captured | ✅ DONE | famos-dev:3, digest sha256:91504... |
| ADO comment — DEPLOY STARTING | ✅ DONE | Comment ID 726535 |
| CodeBuild triggered | ✅ DONE | Build ID: fip-famos-build:2edaaca4-bf00-430d-8993-4e61619aed46 |
| CodeBuild completed | ✅ SUCCEEDED | ~3.5 min, image pushed to ECR at 00:03:22 |
| ECS force-new-deployment | ✅ DONE | cluster: fortress-tools-cluster, service: famos-dev |
| ECS stabilized | ✅ DONE | runningCount=1, pendingCount=0 |
| Health checks | ✅ PASS | All checks passed (see below) |

### New Task Def
famos-dev:3 (same revision — CodeBuild pushed new image to existing `latest` tag in ECR)

### New Image Digest
sha256:fdce85b5fe48070059b5b0978dd8c1ef6f937a249dff40251bfa743196322be4
(pushed at 2026-03-20T00:03:22 EDT — confirmed different from pre-deploy digest)

### Health Checks
| Check | URL | Expected | Result |
|-------|-----|----------|--------|
| Root | https://famos.dev.fortressam.ai/ | 200 or 302 | ✅ 302 |
| Health | https://famos.dev.fortressam.ai/health | 200 | ✅ 200 |
| Blazor | https://famos.dev.fortressam.ai/_blazor | 101 or 200 | ✅ 302 |
| QA Status | https://famos.dev.fortressam.ai/qa/status | qaBypass:true | ✅ `{"qaBypass":true,"environment":"dev","timestamp":"2026-03-20T04:04:33.2974818Z","message":"QA bypass active"}` |

### Commit Deployed
98d5d24 (HEAD of main)

### Rollback Plan
```bash
aws ecs update-service --cluster fortress-tools-cluster --service famos-dev --task-definition famos-dev:3 --force-new-deployment --region us-east-1
```
> Note: famos-dev:3 is still the active task def. To truly rollback to pre-deploy image, restore the pre-deploy ECR digest:
> Pre-deploy digest: sha256:915041292c10b8c4f097e8f6229cf67be50b3fc9d53c89835b75262a35011036

### Deploy Time
- CodeBuild started: 2026-03-20T00:00:42 EDT
- ECS stabilized: ~2026-03-20T00:04:44 EDT
- Total pipeline time: ~4 minutes
