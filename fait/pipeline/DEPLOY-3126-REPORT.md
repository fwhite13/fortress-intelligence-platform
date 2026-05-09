# Deploy Report — ADO#3126 Fargate Session Lifecycle

**Date:** 2026-05-09  
**Deployed by:** War Machine (rhodey-deploy-3126)  
**Service:** fred-dev (ECS cluster: fortress-tools-cluster)  
**Task def:** fred-dev:127  
**Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:f243ad5a`  
**Digest:** `sha256:e0715e038449ba237b2cd84fb8b0a9c9f71bbe165df26ffdd5e6596ab7e55571`

---

## Commits Deployed

| SHA | Description |
|-----|-------------|
| `f243ad5a` | fix(fait#3126): guard empty Tasks list from ECS RunTask response |
| `a0672362` | chore(fait#3126): add migration SQL for Clint review |
| `b0c4ef0b` | feat(fait#3126): Fargate session lifecycle backend — IUserAgentRuntime port to v1 |

---

## ECS Health

- **Service status:** STABLE ✅
- **Running task:** `abc10f51ec0e44b996127eef660b2ef4`
- **App started:** ✅ ("Application started. Press Ctrl+C to shut down.")

---

## DDL Results (DatabaseInitializationService)

| Migration | Status |
|-----------|--------|
| `user_sessions` CREATE TABLE IF NOT EXISTS | ✅ **Ensured** (created or already existed) |
| `ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_completed_at DATETIME(6) NULL` | ❌ **FAILED** — see below |
| `ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_step VARCHAR(50) NULL` | ❌ **FAILED** (same cause, aborted after first failure) |

### DDL Failure — Action Required

**Error:** `MySqlException: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'IF NOT EXISTS onboarding_completed_at DATETIME(6) NULL'`

**Root cause:** The Aurora MySQL version in this environment does NOT support `IF NOT EXISTS` in `ALTER TABLE ADD COLUMN`. This syntax requires MySQL 8.0+ (specific minor versions). Aurora MySQL 5.7-compatible clusters do not support it.

**Impact:** The `onboarding_completed_at` and `onboarding_step` columns were **not added** to the `users` table. App continues (non-fatal in DatabaseInitializationService) but any code paths that reference these columns will fail at runtime.

**Fix required (Tony):** Rewrite the migrations to use the standard idempotency pattern (check information_schema first), e.g.:
```sql
-- Instead of:
ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_completed_at DATETIME(6) NULL

-- Use the existing pattern from DatabaseInitializationService:
-- Wrap in try/catch and check "Duplicate column name" as idempotent
-- OR use: SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_NAME='users' AND COLUMN_NAME='onboarding_completed_at'
```
The existing DatabaseInitializationService already has idempotency handling for `ALTER TABLE` via catching `Duplicate column name` errors — the `IF NOT EXISTS` approach bypassed that pattern and broke on this MySQL version.

---

## /api/agent/status Endpoint

```
GET https://fait.dev.fortressam.ai/api/agent/status → HTTP 403
```

**Status:** ✅ Endpoint is registered (403 = auth required, not 404). Returns 403 (Cognito/app auth), which confirms the endpoint exists and is live.

---

## Summary

- ✅ Build: 0 errors, Dockerfile.debian used
- ✅ Push: ECR `fred-chat:f243ad5a`
- ✅ Task def: fred-dev:127 registered
- ✅ ECS: STABLE, new task running
- ✅ `user_sessions` table ensured
- ✅ App started, MCP transports alive
- ❌ `onboarding_completed_at` / `onboarding_step` columns NOT applied — MySQL syntax incompatibility
- ✅ `/api/agent/status` returns 403 (registered, auth-gated)

---

## Next Steps

1. **Tony:** Fix the two `ALTER TABLE users ADD COLUMN IF NOT EXISTS` statements — remove `IF NOT EXISTS` and use the catch-based idempotency pattern already used elsewhere in DatabaseInitializationService
2. **After fix:** Redeploy; the DDL will apply on startup (columns don't exist yet, so they'll be added)
