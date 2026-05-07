# Review Report — ADO#2877

**Task:** FAIT v2 Scheduled Tasks — DB schema + cron service  
**Commit:** `3132b9f`  
**Cycle:** 1  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-07

---

### Verdict: PASS

---

### Spec Compliance Check

All 13 spec check points verified via Claude Code CLI review.

---

### Check-by-Check Results

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 1 | `ScheduledTask.Id` / `UserId` are `string` with `Guid.NewGuid().ToString()` | ✅ PASS | `ScheduledTask.cs:5` — correct type, no format specifier |
| 2 | EF: `HasMaxLength(36)` on all string ID columns | ✅ PASS | `FaitV2DbContext.cs:312-313, 338-339`; migration cols are `varchar(36)` |
| 3 | Migration uses Core API only — no raw SQL | ✅ PASS | All `migrationBuilder.CreateTable()`, `CreateIndex()`, etc. |
| 4 | `ScheduledTaskService` filters ALL queries by `userId` | ✅ PASS | Every method (`Get`, `Update`, `Delete`, `TriggerNow`, `GetRunHistory`) includes `userId` in the predicate |
| 5 | CAS distributed lock via `ExecuteSqlRawAsync` with correct WHERE | ✅ PASS | `ScheduledTaskBackgroundService.cs:73-77` — `WHERE id = {0} AND (last_run_status != 'running' OR last_run_at < DATE_SUB(..., INTERVAL 30 MINUTE))` |
| 6 | `claimed == 0` → skip | ✅ PASS | Lines 79-83, early return |
| 7 | Cronos API — `CronExpression.Parse` + `GetNextOccurrence` | ✅ PASS | Lines 192-193 — correct Cronos 0.8 API |
| 8 | Retry after 5 min on 1st failure; deactivate on 2nd | ✅ PASS | Lines 136-147 (result path) and 170-177 (exception path) — `FailureCount` incremented before check; `== 1` → retry, else → `IsActive = false` |
| 9 | `IScheduledTaskService` registered as `Scoped` | ✅ PASS | `Program.cs:171` |
| 10 | `ScheduledTaskBackgroundService` registered as `AddHostedService` | ✅ PASS | `Program.cs:172` |
| 11 | Build: 0 errors | ✅ PASS | Confirmed — 0 errors, 0 warnings |
| 12 | No Cognito references | ✅ PASS | Clean |
| 13 | No hardcoded user IDs | ✅ PASS | All queries use parameter |

---

### Critical Issues
None.

---

### Important Issues
None.

---

### Nitpicks

- **N1:** `FaitV2DbContext.cs` has a duplicate `AgentPlugin` entity configuration block (lines ~289–305 and ~355–372). The second block silently overrides the first — different `HasMaxLength` for `Name`, different column types for `AllowedMcpServers`/`AllowedRoles`, missing unique index on `Name`. **Pre-existing and out of scope for ADO#2877** — should be cleaned up in a follow-on ticket.

---

### Positive Observations

- CAS lock pattern is correct and robust — INTERVAL 30 MINUTE guard protects against stale locks from crashed instances.
- Failure retry logic (`FailureCount == 1` → retry, else → deactivate) is clean and matches spec exactly.
- userId isolation is thorough — no query path bypasses the predicate filter.
- Build is clean with zero warnings.

---

### Acceptance Criteria Verification

All 13 criteria verified. Feature is correct, secure, and safe to merge.
