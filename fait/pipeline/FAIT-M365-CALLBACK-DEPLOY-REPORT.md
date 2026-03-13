# Deploy Report: FAIT M365 Callback Alias + MCP Logging Fixes

**Date:** 2026-03-13  
**Time:** 23:58–00:03 EDT  
**Agent:** War Machine (Rhodey) — devops subagent  
**Authorized by:** Maria Hill  
**Pipeline:** FAIT (fred-dev ECS service)

---

## Commits Deployed

| Commit | Description |
|--------|-------------|
| `c67fcc0` | ChatView ILogger injection; MCP tool load error → `LogError`, success → `LogInformation` with tool list |
| `b1cad64` | `/auth/ms-callback` route alias (Option A delegate) — reviewed PASS 8/8 |

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Running task definition | `fred-dev:67` |
| Running image digest | `sha256:d0ca9357…` |
| Service | `fred-dev` on cluster `fortress-tools-cluster` |
| Health baseline | Healthy |

---

## Build

| Item | Value |
|------|-------|
| CodeBuild project | `fip-fait-build` |
| Build ID | `fip-fait-build:2572a34f-7085-4ce8-bd7a-a1efe46bd729` |
| Build start | 23:58:08 EDT |
| Build end | 23:59:51 EDT |
| Build duration | ~1m 45s |
| Build status | ✅ **SUCCEEDED** |
| New ECR image tag | `kb-latest` |
| New ECR digest | `sha256:6bc291b599c26a1682148ca93397234d16ddb188ebc194d2948e7017911bb373` |

---

## Deploy

| Item | Value |
|------|-------|
| ECS update-service triggered | 00:00:01 EDT |
| New task definition | `fred-dev:69` |
| Rollout start state | `IN_PROGRESS` |
| Rollout complete | 00:02:52 EDT |
| Rollout duration | ~2m 50s |
| Rollout state | ✅ **COMPLETED** |
| Running count | 1 |

---

## Verification

| Check | Result |
|-------|--------|
| ECR digest | `sha256:6bc291b599c26a1682148ca93397234d16ddb188ebc194d2948e7017911bb373` |
| Task digest | `sha256:6bc291b599c26a1682148ca93397234d16ddb188ebc194d2948e7017911bb373` |
| Digest match | ✅ **MATCH** |
| Health endpoint | `https://fait.dev.fortressam.ai/health` |
| Health response | `{"status":"healthy","service":"fred","timestamp":"2026-03-13T04:02:58.3652048Z"}` |
| Health status | ✅ **HEALTHY** |

---

## Pipeline Timeline

| Time (EDT) | Event |
|------------|-------|
| 23:58:08 | CodeBuild started |
| 23:59:51 | CodeBuild SUCCEEDED |
| 00:00:01 | ECS update-service triggered → `fred-dev:69` |
| 00:02:52 | ECS rollout COMPLETED |
| 00:03:xx | Digest match verified, health check HEALTHY |

**Total pipeline time:** ~5 minutes

---

## Rollback Plan

If rollback is needed:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer

aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:67 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

Verify rollback health:
```bash
curl -sf https://fait.dev.fortressam.ai/health && echo "✅ HEALTHY" || echo "❌ HEALTH FAILED"
```

---

## Outcome

✅ **DEPLOYED SUCCESSFULLY**

- `fred-dev:69` is live with digest match confirmed
- M365 `/auth/ms-callback` route alias active
- MCP logging improvements (LogError on failure, LogInformation with tool list on success) active
- Service healthy at `https://fait.dev.fortressam.ai/health`
