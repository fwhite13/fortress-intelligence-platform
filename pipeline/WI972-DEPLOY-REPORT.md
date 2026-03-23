# Deploy Report: WI#972 — FAM OS Task Center Fix

**Date:** 2026-03-20  
**Agent:** War Machine (James Rhodes)  
**Work Item:** FAIT#972  
**Commit:** 8faf09f  
**Environment:** famos-dev (https://famos.dev.fortressam.ai)

---

## Summary

Removed `FAMOS_QA_BYPASS=true` env var from the `famos-dev` ECS task definition and force-deployed to fix the FAM OS Task Center.

---

## Pre-Deploy Snapshot

| Property | Value |
|----------|-------|
| Previous task def revision | `famos-dev:3` |
| FAMOS_QA_BYPASS present | `true` |
| Env var count | 11 |

---

## Job 1 — Remove FAMOS_QA_BYPASS from Task Def

| Step | Result |
|------|--------|
| Fetched current task def `famos-dev:3` | ✅ |
| Confirmed `FAMOS_QA_BYPASS = true` present | ✅ |
| Stripped `FAMOS_QA_BYPASS` from container env | ✅ (11 → 10 vars) |
| Registered new task def revision | ✅ `famos-dev:4` |

---

## Job 2 — Force Deploy

| Step | Result |
|------|--------|
| Updated ECS service to `famos-dev:4` | ✅ |
| Forced new deployment | ✅ |
| Waited for service stable | ✅ Stable |
| Health check `https://famos.dev.fortressam.ai/health` | ✅ **HTTP 200** |

---

## Verification

```
FAMOS_QA_BYPASS present: False
All env var names: ['HubSpot__ServiceKey', 'FORTRESS_DB_PORT', 'ASPNETCORE_ENVIRONMENT',
  'FIP__LoginUrl', 'ASPNETCORE_URLS', 'FORTRESS_DB_HOST', 'FIP_KEYRING_DB_NAME',
  'Auth__CookieDomain', 'FAMOS_DB_NAME', 'FORTRESS_DB_USER']
```

`FAMOS_QA_BYPASS` is confirmed **absent** from `famos-dev:4`.

---

## Rollback Plan

If health degrades: redeploy `famos-dev:3`

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service famos-dev \
  --task-definition "famos-dev:3" \
  --force-new-deployment \
  --region us-east-1
```

> Note: `famos-dev:3` contains `FAMOS_QA_BYPASS=true` — task center will be broken but app will be healthy.

---

## Outcome

✅ **DEPLOY SUCCESSFUL**  
- Task def: `famos-dev:4` (active)  
- FAMOS_QA_BYPASS: removed  
- Health: 200  
- ECS service: stable  
