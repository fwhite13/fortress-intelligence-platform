# FAIT M365 OAuth Fixes — Deploy Report

**Task ID:** FAIT-M365-OAUTH-FIXES  
**Deployed By:** War Machine (DevOps)  
**Deployment Date:** 2026-03-13 01:18 EDT  
**Status:** ✅ **DEPLOYED**

---

## What Changed

### Commits Deployed
- **`6e93b67`** — TenantId trailing slash trim (fixes double-slash in OAuth URL)
- **`0e3fb22`** — DevOps DDL collation fix
- **`9359b54`** — M365 redirect URI read from `MicrosoftGraph:RedirectUri` config (both callback + Settings.razor)

### Fixes
1. **OAuth URL malformed redirect** — TenantId no longer adds trailing slash, preventing double-slash in redirect URI
2. **Configuration-driven redirect URI** — Redirect URI now read from `MicrosoftGraph:RedirectUri` config instead of hardcoded
3. **Database collation compliance** — DevOps DDL adjusted for proper collation handling

---

## Pre-Deployment Snapshot

| Metric | Value |
|--------|-------|
| Current Service | `fred-dev` (fortress-tools-cluster) |
| Running Task Definition | `fred-dev:69` |
| Running Image Digest | `sha256:c6030bbd…` (previous build) |
| Service Status | ACTIVE (1 desired, 1 running) |
| Health Check | ✅ PASSING |

---

## Build & Deployment Timeline

| Step | Time | Duration | Status |
|------|------|----------|--------|
| CodeBuild triggered | 21:02:45 | — | ✅ STARTED |
| CodeBuild SUCCEEDED | 21:04:49 | 2m 4s | ✅ PASSED |
| ECR push kb-latest | 21:04:49 | — | ✅ COMPLETED |
| Task definition rev 65 → 66 (new digest) | 21:13:34 | — | ✅ REGISTERED |
| ECS update-service forced deployment | 21:13:34 | — | ✅ TRIGGERED |
| Task deployment rolled out | 21:18:23 | 4m 49s | ✅ COMPLETED |
| Health check validation | 21:18:34 | — | ✅ PASSING |

---

## Post-Deployment Verification

### Service State
| Metric | Value |
|--------|-------|
| **New Task Definition** | `fred-dev:67` |
| **Running Task** | `b225ba20656c49ea82f6e5d1c33aaab8` |
| **New Image Digest** | `sha256:77049bed86012e80cf4038a3dab4566db853f5dd667ed16a750260ba69b6f695` |
| **Task Status** | RUNNING (started 21:16:52 EDT) |
| **Service Desired Count** | 1 |
| **Service Running Count** | 1 |
| **Deployment State** | COMPLETED ✅ |

### Digest Verification
```
Task digest:    sha256:77049bed86012e80cf4038a3dab4566db853f5dd667ed16a750260ba69b6f695
ECR digest:     sha256:77049bed86012e80cf4038a3dab4566db853f5dd667ed16a750260ba69b6f695
✅ DIGEST MATCH
```

### Health Checks
```
✅ Service health endpoint: PASSING
   Response: {"status":"healthy","service":"fred","timestamp":"2026-03-13T01:18:34.7443558Z"}

✅ HTTP endpoint: RESPONDING
   Response: HTTP/2 405 (expected for GET to root, service is operational)
```

---

## Rollback Plan

If critical issues are detected post-deployment, execute immediate rollback:

```bash
# Rollback to previous task definition (fred-dev:69)
source ~/projects/ai/projects/fortress_tools/.env.deployer

aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:69 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer

# Verify rollback
aws ecs describe-services \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1 \
  --profile fortress-tools-deployer \
  --query 'services[0].deployments[0].{state:rolloutState,running:runningCount,taskDef:taskDefinition}' \
  --output text

# Verify health
curl -sf https://fait.dev.fortressam.ai/health && echo "✅ ROLLED BACK & HEALTHY"
```

---

## Deployment Summary

✅ **DEPLOYMENT SUCCESSFUL**

- CodeBuild completed M365 OAuth fixes and pushed new image to ECR
- New task definition registered with updated image digest
- Service successfully rolled out to new container
- Post-deployment health checks PASSING
- Digest verification CONFIRMED
- **Ready for QA verification** (Black Widow)

---

## Next Steps

1. **QA Verification** → Black Widow to verify M365 OAuth redirect flow and Settings.razor config binding
2. **Smoke Tests** → Confirm OAuth login flow works with new redirect URI configuration
3. **Regression Tests** → Verify no impact to existing auth/data handling
4. **Sign-off** → Once verified, pipeline complete

---

**Deployed by:** War Machine (Rhodey)  
**Deployment Status:** ✅ COMPLETE — HEALTHY — READY FOR VERIFICATION
