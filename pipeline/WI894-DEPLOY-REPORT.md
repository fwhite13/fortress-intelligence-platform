# Deploy Report: WI894 — FAM OS Sprint 4 (Aurora Migration Fix, Retry 2)

**Agent:** War Machine (James Rhodes)  
**Date:** 2026-03-19  
**Commit:** `6862ee1`  
**Deploy Attempt:** Retry 2  

---

## Outcome: ⚠️ PARTIAL — New Blocker Identified

The crash-loop is resolved. ECS is stable (1/1). Health endpoint is 200. However, a **new runtime error** is now surfacing in `SignalRecomputeService`: the `intake_responses_json` column does not exist in the Aurora DB, meaning the migration try/catch **suppressed the error but the column was never added**.

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Previous task def | `famos-dev:3` (rollback target) |
| Previous image | prior fip-famos-build artifact |
| Health baseline | N/A (service was crash-looping) |

---

## Build

| Item | Value |
|------|-------|
| CodeBuild project | `fip-famos-build` |
| Build ID | `fip-famos-build:8d7c95f4-e584-43e5-a172-4f88278bcd86` |
| Build status | **SUCCEEDED** |
| Build duration | ~2 minutes |

---

## ECS Stabilization

| Item | Value |
|------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `famos-dev` |
| Running / Desired | **1 / 1** ✅ |
| Task definition | `famos-dev:1` |

---

## Health Checks

| Check | Status |
|-------|--------|
| `https://famos.dev.fortressam.ai/health` | **200** ✅ |
| Body | `{"status":"healthy","service":"famos","timestamp":"2026-03-19T18:11:18.8247987Z"}` |
| `fip-tokens.css` | **200** ✅ |

---

## Migration Log Analysis

**Log stream:** `famos-web/famos-web/54c05463a2864b83af5f9e7e93970c6d`

Startup log output:
```
Using Aurora MySQL: fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com/famos_dev
Application started.
[FAM OS] DB tables already exist.
```

**Observation:** The log line `[FAM OS] DB tables already exist.` indicates the migration code hit the "tables exist" branch and **did not attempt to add the new column**. The try/catch on error 1060 was never reached — the column add was skipped entirely because the table existence check short-circuited the migration path.

---

## ⚠️ New Blocker: `intake_responses_json` Column Missing

**Error (repeating in `SignalRecomputeService`):**
```
MySqlConnector.MySqlException (0x80004005): Unknown column 'o.intake_responses_json' in 'field list'
  at FamOs.Web.Services.SignalRecomputeService.RecomputeAllAsync()
     in /src/famos/src/FamOs.Web/Services/SignalRecomputeService.cs:line 43
```

**Root Cause:** The migration logic uses a guard: *"if tables already exist, skip migration."* This guard was written for initial DB setup but doesn't handle **additive schema changes** (new columns on existing tables). When `famos_dev` already has the core tables from a prior deploy, the entire migration block — including the `ADD COLUMN` for `intake_responses_json` — is skipped.

**The try/catch on 1060 from commit `6862ee1` is correct for Aurora compatibility but was never executed** because the outer table-existence check bailed out first.

**Fix Required (WI894 needs another build):**
The column-add logic must be extracted from the "initial setup" guard and run **unconditionally** (or in a separate "schema upgrade" block) so it executes even when tables already exist. The try/catch on 1060 should be retained for Aurora compat.

Suggested pattern:
```csharp
// Always run column additions, even if tables exist
try {
    await db.ExecuteSqlAsync("ALTER TABLE orders ADD COLUMN intake_responses_json LONGTEXT NULL");
} catch (MySqlException ex) when (ex.Number == 1060) {
    // Column already exists — Aurora compat, safe to ignore
}
```

---

## Rollback Plan

Service is currently **running and healthy** (not crash-looping), so immediate rollback is not required. The `SignalRecomputeService` errors are logged but non-fatal to the health endpoint.

If rollback is needed:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service famos-dev \
  --task-definition famos-dev:3 \
  --region us-east-1
```

---

## Summary

| Stage | Result |
|-------|--------|
| CodeBuild | ✅ SUCCEEDED |
| ECS deploy | ✅ 1/1 stable |
| Health endpoint | ✅ 200 |
| fip-tokens.css | ✅ 200 |
| Startup crash-loop | ✅ RESOLVED |
| Migration (column add) | ❌ SKIPPED — table-existence guard short-circuits column add |
| `SignalRecomputeService` | ❌ Runtime error — `intake_responses_json` column missing |

**Verdict: PARTIAL SUCCESS.** Crash-loop fixed. New blocker: column add logic must run outside the table-existence guard. Requires one more build.

---

*War Machine out.*
