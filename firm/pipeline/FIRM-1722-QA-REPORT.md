# QA Report — FIRM ADO #1722 — SharePanel KB Push Fix

**Analyst:** Natasha Romanoff (Black Widow — QA)  
**Date:** 2026-04-13  
**Start:** 15:19 EDT | **End:** 15:31 EDT | **Duration:** ~12 minutes  
**Verdict:** ❌ **FAIL — Deploy defect: #1722 fixes NOT present in deployed image**

---

## Environment

| Field | Value |
|-------|-------|
| Target URL | https://firm.dev.fortressam.ai |
| Service | firm-web |
| ECS Task Def | firm-web:82 |
| ECR Image Tag | `6030c7ef1e0e78f5bbda5aaa9ad823410c316346` |
| Image Pushed At | 2026-04-13 14:43 EDT |
| Test Start | 2026-04-13 15:19 EDT |

---

## Smoke Tests

| Test | Result | Details |
|------|--------|---------|
| Health endpoint | ✅ PASS | `GET /health` → HTTP 200, `{"status":"healthy","service":"firm"}` |
| App startup errors (CloudWatch) | ✅ PASS | 99 log events, no `InvalidOperationException`, no `Exception` of any kind |
| DB initialization | ✅ PASS | All tables ensured, migrations applied idempotently |
| App listens | ✅ PASS | `Now listening on: http://[::]:8080`, host env: Development |

---

## Targeted Tests

### TC1 — KB Push via SharePanel (CRITICAL)

| Test | Result | Details |
|------|--------|---------|
| #1722 fixes in deployed image | ❌ **FAIL** | `SharePanel.razor` in deployed image still contains `HttpClientFactory.CreateClient("local")` (8 occurrences confirmed via git show) |
| S3Service DI lifetime fix | ❌ **FAIL** | `Program.cs` fix (`AddScoped→AddSingleton`) NOT in deployed image |

**Verdict: TC1 = FAIL**

### TC2 — Startup Health / No `InvalidOperationException` (IMPORTANT)

| Test | Result | Details |
|------|--------|---------|
| Startup log scan (CloudWatch) | ✅ PASS | Zero exceptions in startup logs. App started cleanly. |
| S3Service DI bug triggered? | ⚠️ N/A | S3Service DI fix not in deployed image, but no exception thrown — likely the S3Service was never exercised at startup OR the captive dependency bug requires a request to trigger it |

**Verdict: TC2 = CONDITIONAL PASS** (startup clean, but fix is not deployed)

---

## Root Cause: Deploy Defect

### What happened

The C4 CodeBuild (build #52, `fip-firm-build:f46fbeba`) ran at **15:15–15:18 EDT**. It built and pushed ECR image `9b44e90` at **15:16**. However, the `firm-web:82` task definition (registered at 15:17) was deployed with image tag **`6030c7e`** (built at 14:43 — 34 minutes before the deploy).

This means the buildspec/deploy script registered the task def with the **previous image tag** instead of the newly-built one.

### Evidence

| Item | Expected | Actual |
|------|----------|--------|
| ECR image built by C4 | `9b44e90...` (pushed 15:16) | Not deployed |
| ECR image in firm-web:82 task def | `9b44e90...` | `6030c7e...` (pushed 14:43) |
| SharePanel.razor in deployed image | Zero `HttpClientFactory` calls | 8 `HttpClientFactory.CreateClient("local")` calls present |
| S3Service registration in deployed image | `AddSingleton` | `AddScoped` (unverified, but `6030c7e` predates the fix) |

### Git Commit Timeline

| Time (EDT) | Commit | Contents |
|------------|--------|----------|
| 14:17 | `6030c7e` | nexus#1726/#1727 fix (PRE-1722) — **this is what firm-web:82 runs** |
| 15:00 | `0edf3b1` | #1722 SharePanel HttpClient removal |
| 15:07 | `9b44e90` | #1785 SummaryText markdown (also includes 0edf3b1) |
| 15:09 | `ba00149` | #1722 S3Service AddScoped→AddSingleton |
| 15:15 | C4 build starts | CodeBuild cloned main (captured `9b44e90` as HEAD, missed `ba00149` by ~8min) |
| 15:16 | ECR push | Image `9b44e90` pushed — includes SharePanel fix but NOT S3Service fix |
| 15:17 | firm-web:82 registered | Task def uses `6030c7e` (old image) — neither fix deployed |

### Additional Issue

Even if the C4 build's image (`9b44e90`) had been deployed correctly, it would be **missing the S3Service DI fix** (`ba00149`) which was committed ~8 minutes before the build started but wasn't yet pushed when CodeBuild cloned.

---

## Issue Summary

### CRITICAL: #1722 SharePanel Fix Not Deployed
- **What:** `firm-web:82` runs image `6030c7e` (14:43 EDT), which predates both #1722 commits
- **Expected:** `SharePanel.razor` with zero `HttpClientFactory` calls + `Program.cs` with `AddSingleton<S3Service>`
- **Actual:** `SharePanel.razor` has 8 `HttpClientFactory.CreateClient("local")` calls — KB push will return 403 in production
- **Impact:** TC1 critical path broken. KB push will fail with 403 for all users.
- **Action Required:** Re-deploy with correct image (must include both `0edf3b1` AND `ba00149`)

---

## What Needs to Happen

1. **Rollback or re-deploy.** The current `firm-web:82` does not contain the fixes. Options:
   - Option A: Re-trigger CodeBuild after `ba00149` is the current `main` HEAD → produces an image with both fixes → deploy that image as `firm-web:83`
   - Option B: Roll back to `firm-web:81` (same image, no regression) while fix is re-built

2. **Fix the deploy script.** The buildspec/ECS update step wrote the old image tag into the task def instead of the freshly-built one. This should be investigated so C5 doesn't have the same issue.

---

## Test Summary

| Category | Tests | Passed | Failed |
|----------|-------|--------|--------|
| Smoke | 4 | 4 | 0 |
| Targeted | 2 | 0 | 2 |
| **Total** | **6** | **4** | **2** |

---

**Verdict: ❌ FAIL**  
TC1: FAIL — #1722 fixes not in deployed image (deploy defect).  
TC2: CONDITIONAL PASS — No startup exceptions logged, but S3Service DI fix also not deployed.  

Do NOT close WI #1722. Re-deploy required.

---

_Trust nothing. Verify everything. — Black Widow_
