# QA Re-Check Report — WI894
**Date:** 2026-03-19  
**Agent:** Black Widow (Natasha Romanoff) — `qa-analyst`  
**URL:** https://famos.dev.fortressam.ai  
**ADO WI:** 894  

---

## Verdict: ⚠️ WARN

Service is healthy and accepting traffic. Column exists in Aurora. However, the running ECS container is still throwing `Unknown column 'o.intake_responses_json'` in `SignalRecomputeService` — the deployed image has not picked up the migration.

---

## Test Results

### T1 — Health Check ✅ PASS
```
curl -sk https://famos.dev.fortressam.ai/health
→ {"status":"healthy","service":"famos","timestamp":"2026-03-19T18:14:26.3791374Z"}
```
**Result:** `healthy` — 200 OK.

---

### T2 — Column Exists ✅ PASS
```sql
SHOW COLUMNS FROM opportunities LIKE 'intake_responses_json';
→ Field: intake_responses_json | Type: mediumtext | Null: YES | Default: NULL
```
**Result:** Column present in Aurora `famos_dev.opportunities`. Matches expected `mediumtext YES NULL`.

---

### T3 — ECS Logs: SignalRecomputeService Errors ⚠️ WARN
Checked 2 most recent log streams from `/famos/tasks`:

- `famos-web/famos-web/54c05463a2864b83af5f9e7e93970c6d` — no relevant hits
- `famos-web/famos-web/d915d6f7f70f4493a4b9878f0bc12dc3` — **ACTIVE ERRORS FOUND**

```
fail: FamOs.Web.Services.SignalRecomputeService[0]
  [SignalRecompute] Error during recompute run
  MySqlConnector.MySqlException (0x80004005): Unknown column 'o.intake_responses_json' in 'field list'
  at FamOs.Web.Services.SignalRecomputeService.RecomputeAllAsync() in .../SignalRecomputeService.cs:line 43
  at FamOs.Web.Services.SignalRecomputeService.ExecuteAsync(CancellationToken ct) in .../SignalRecomputeService.cs:line 27
```

**Root cause:** The ECS task running `SignalRecomputeService` was built before the `intake_responses_json` migration landed (or the migration ran after the current image was deployed). The column exists in the DB but the running container's EF Core model is querying it in a way that Aurora is rejecting — likely a schema/image version mismatch (EF model references the column but the DB connection/server the container is hitting may differ, OR the container image itself predates an EF model update that references the column).

**Impact:** `SignalRecomputeService` background job is failing on every recompute cycle. Signal recomputation is degraded. Core app health and routing are unaffected.

**Non-blocking:** Yes — service passes health check, /tasks route works. Feature degraded only.

---

### T4 — /tasks Route ✅ PASS
```
curl -sk -o /dev/null -w "%{http_code}" https://famos.dev.fortressam.ai/tasks
→ 302
```
**Result:** Auth redirect as expected.

---

## Summary

| Test | Result | Notes |
|------|--------|-------|
| T1 — Health | ✅ PASS | `{"status":"healthy"}` |
| T2 — Column exists | ✅ PASS | `mediumtext YES NULL` in Aurora |
| T3 — ECS logs clean | ⚠️ WARN | `Unknown column 'o.intake_responses_json'` in SignalRecomputeService — ongoing |
| T4 — /tasks 302 | ✅ PASS | Auth redirect |

---

## Recommendation

The column migration is confirmed in Aurora, but the running ECS image is still erroring. A **redeployment** (force new deployment or image rebuild) is needed to clear this. Either:
1. The deployed image doesn't include the EF migration that adds the column reference, **or**
2. The image does reference it but is connecting to a different DB endpoint that hasn't been migrated.

Recommend Rhodey force a new ECS deployment with the latest image to clear the mismatch.

---

*QA Re-Check complete. WARN verdict — non-blocking, action recommended.*
