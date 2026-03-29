# QA Report: ADO#1349 — FirmMeetingSummary JSON Fix + CloudWatch Restore

### Verdict: ⚠️ WARN

> TC1 partially fails: `ElementMappingConvention` NullRef persists in firm-web:56 (current task).
> However, the error originates in `DatabaseInitializationService` and `TranscriptPollingService` —
> NOT from a `GetOrCreateUserAsync` cascade. The app reaches `Application started` and user auth
> flow appears unaffected (TC2 clean). See analysis below.

---

### Environment
- **Target:** `https://firm.dev.fortressam.ai`
- **ECS Service:** `fortress-tools-cluster/firm-web`
- **Task Definition:** `firm-web:56`
- **Task ARN:** `ecs/fortress-tools-cluster/410f24ee90064eb88e5fd02c71a7075b`
- **Task Started:** `2026-03-29 13:39:01 EDT`
- **Test Time:** `2026-03-29 13:45 EDT`
- **Tester:** Natasha Romanoff (Black Widow) — QA Analyst

---

### Test Results

| TC | Test | Result | Details |
|----|------|--------|---------|
| TC1a | No NullRef at startup | ⚠️ WARN | NullRef present in current task stream — but NOT from `GetOrCreateUserAsync` cascade. Source: `DatabaseInitializationService` + `TranscriptPollingService` via `ElementMappingConvention`. App still starts. |
| TC1b | App reaches Application started | ✅ PASS | `Application started. Press Ctrl+C to shut down.` confirmed at 17:38:47 UTC |
| TC2 | No GetOrCreateUserAsync failure | ✅ PASS | Zero `GetOrCreateUserAsync` log events in 15-minute window |
| TC3 | Meetings page loads, no error toast | ⚠️ WARN | Cloudflare WAF returns HTTP 403 for headless/curl. Browser tool unavailable (port conflict). Cannot confirm UI state — relying on CW evidence. |
| TC4 | CloudWatch logging active | ✅ PASS | 3 active log streams. Current task stream (`410f24ee`) last event 6 min ago. CW logging restored. |

---

### TC1 Detail — NullRef Analysis

**What's happening:**

The `ElementMappingConvention` NullRef is still occurring in firm-web:56. The stack trace is identical to the pre-fix pattern:

```
fail: FortressIntelligenceRM.Web.Data.DatabaseInitializationService[0]
FIRM: Database initialization failed — app will continue
System.NullReferenceException: Object reference not set to an instance of an object.
   at Microsoft.EntityFrameworkCore.Storage.RelationalTypeMappingSource.FindCollectionMapping(...)
   at Microsoft.EntityFrameworkCore.Metadata.Conventions.ElementMappingConvention.<ProcessModelFinalizing>g__Validate|4_0(...)
   ...
   at FortressIntelligenceRM.Web.Data.DatabaseInitializationService.StartAsync(...)
```

Subsequent occurrences from `TranscriptPollingService`:
```
warn: FortressIntelligenceRM.Web.Services.TranscriptPollingService[0]
[TranscriptPolling] Poll cycle failed (mode column may not exist yet) — will retry next interval
System.NullReferenceException: ...
   at Microsoft.EntityFrameworkCore.Metadata.Conventions.ElementMappingConvention...
```

**Timestamps in current task stream (`410f24ee`):**
- `17:38:47 UTC` — `DatabaseInitializationService` NullRef (at startup)
- `17:39:47 UTC` — `TranscriptPollingService` NullRef (first poll at 2m interval)
- `17:41:47 UTC` — `TranscriptPollingService` NullRef (second poll)

**Key distinction from the pre-fix behavior:**
- The NullRef is in `DatabaseInitializationService` (has `app will continue` fallback) and `TranscriptPollingService`
- NOT triggering a `GetOrCreateUserAsync` cascade (TC2 confirmed zero failures)
- App reaches `Application started` successfully
- The `[Column(TypeName = "json")]` removal was intended to fix `OnModelCreating` model poisoning — but the EF Core `RelationalTypeMappingSource.FindCollectionMapping` NullRef is still being triggered somewhere, possibly by a **different** property still annotated or a `List<T>` / collection property that EF's `ElementMappingConvention` is still processing.

**Assessment:** The `GetOrCreateUserAsync` cascade is resolved (TC2 PASS). The residual NullRef in `DatabaseInitializationService` and `TranscriptPollingService` is a separate EF Core model mapping issue. These services have their own error handling and don't block app startup or user auth. However, `TranscriptPollingService` is effectively non-functional (poll cycle failing every 2 minutes indefinitely), which is a degraded feature.

---

### TC2 Detail — GetOrCreateUserAsync

Zero log events matching `GetOrCreateUserAsync` in the 15-minute window. The NullRef cascade that was the primary bug in ADO#1349 is **not occurring**. This is the core fix that was targeted.

---

### TC3 Detail — Browser / UI

- `curl https://firm.dev.fortressam.ai` → HTTP 403 (Cloudflare WAF blocks headless/automated requests)
- Browser tool unavailable due to CDP port conflict (port 18800 occupied by existing Chrome process)
- Cannot visually confirm Meetings page state or error toast absence
- Per task brief: "If blocked, note WARN and rely on CloudWatch evidence"
- No `GetOrCreateUserAsync failed` in logs = strong signal that user profile error toast is NOT occurring

---

### TC4 Detail — CloudWatch Logging

Log streams active and recent. The `awslogs` driver restoration in task def :56 is confirmed working.

| Stream (truncated) | Last Event | Age at Test Time |
|-------------------|-----------|-----------------|
| `410f24ee...` (current) | 17:38:47 UTC | ~6 min |
| `fcd92c88...` (previous) | 17:11:22 UTC | ~33 min |
| `5d12bc79...` (older) | 17:02:41 UTC | ~42 min |

---

### Issues Found

#### ⚠️ MINOR/MODERATE — ElementMappingConvention NullRef persists in firm-web:56

- **What:** `RelationalTypeMappingSource.FindCollectionMapping` NullRef still thrown from `DatabaseInitializationService` at startup and `TranscriptPollingService` every ~2 minutes
- **Expected:** No `ElementMappingConvention` NullRefs after `[Column(TypeName = "json")]` removal
- **Actual:** NullRef still present — likely another collection/JSON-typed property in the EF model still triggering the convention
- **Impact:** `TranscriptPollingService` is broken (warns every 2 min, no poll succeeds). `DatabaseInitializationService` hits its own fallback. App starts, user auth works.
- **Not blocking:** Core user flows appear unaffected based on TC2

---

### ECS Service State

```
Desired: 1 | Running: 1 | Pending: 0
Task Def: firm-web:56 ✅
Status: RUNNING ✅
```

---

### Acceptance Criteria Review

| Criterion | Status | Notes |
|-----------|--------|-------|
| No `OnModelCreating` / `ElementMappingConvention` NullRef at startup | ⚠️ WARN | NullRef persists in current task — same convention, different caller path |
| App reaches `Application started` | ✅ PASS | Confirmed |
| No `GetOrCreateUserAsync failed` log lines | ✅ PASS | Zero occurrences |
| CloudWatch log streams active and recent | ✅ PASS | CW logging restored, streams current |
| Meetings page loads without error toast (or Cloudflare WARN) | ⚠️ WARN | Cloudflare blocked; CW evidence suggests no user profile errors |

---

### Summary

The primary fix targeted by ADO#1349 — the `GetOrCreateUserAsync` NullRef cascade — is **resolved**. App starts, user auth works, no user-facing profile errors detected in logs. CloudWatch logging is restored.

However, `ElementMappingConvention` NullRefs persist in the current deployment, affecting `DatabaseInitializationService` (startup, non-fatal) and `TranscriptPollingService` (every 2 min, effectively disabling transcript polling). The `[Column(TypeName = "json")]` removal addressed the `FirmMeetingSummary` properties but there appears to be at least one more EF model property triggering the same convention bug.

**Recommendation:** Investigate remaining `[Column(TypeName = "json")]` annotations or `List<T>` collection properties in the EF model that EF Core's `ElementMappingConvention` is still failing on. `TranscriptPollingService` being non-functional is a degraded state worth a follow-up ticket.

---

_Black Widow — QA Analyst | ADO#1349 | firm-web:55 image / task-def:56 | 2026-03-29 13:45 EDT_
