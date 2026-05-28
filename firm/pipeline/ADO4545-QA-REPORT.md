# QA Report: ADO#4545 — FIRM: JWT Bearer auth for mobile API endpoints (Post-Fix)

**Verdict: ✅ PASS (with human gate on AC2)**  
**Date:** 2026-05-27 22:09 EDT  
**Image:** `firm-web:1fe3cd1c` | Task Def: `firm-web:136`  
**Tester:** Black Widow (QA Analyst)  
**Fix by:** Tony Stark — `OnRedirectToLogin` event handler added to `AddCookie` options in `Program.cs`

---

## Summary

The fix in `1fe3cd1c` is confirmed working. All 4 mobile API endpoints now return clean `401` with **no `Location:` redirect header** — the AC1 failure from `firm-web:135` is resolved. Cookie redirect (`302 → /auth/redirect-to-login`) is intact for all Blazor/non-API routes (AC3 ✅). ECS is HEALTHY on task def `:136`. No JWT startup errors (AC4 ✅). AC2 (live Bearer token) remains a human gate.

---

## Test Results

### Step 1 — ECS Health ✅ PASS

| Check | Result |
|-------|--------|
| Task definition running | `firm-web:136` ✅ |
| ECS status | ACTIVE, 1/1 running |
| Health status | HEALTHY |
| Started at | 2026-05-27 21:56:06 EDT |
| Cloud Map | `firm.fip.internal` → 172.31.40.207 (HEALTHY) ✅ |

### Step 2 — AC1: Mobile Endpoints Return Clean 401 ✅ PASS

Tested via ALB direct (`fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com`) with `Host: firm.dev.fortressam.ai`.  
*(Cloudflare bot protection blocks public curl to `firm.dev.fortressam.ai` — ALB accessed directly as done in prior QA.)*

| Endpoint | HTTP Status | WWW-Authenticate | Location Header | Body | AC1 |
|----------|-------------|-----------------|-----------------|------|-----|
| `GET /api/firm/me` | `401` ✅ | `Bearer` ✅ | **ABSENT** ✅ | Empty | ✅ |
| `GET /api/meetings/list` | `401` ✅ | `Bearer` ✅ | **ABSENT** ✅ | Empty | ✅ |
| `POST /api/meetings/mobile-upload` | `401` ✅ | — (POST, expected) | **ABSENT** ✅ | Empty | ✅ |
| `POST /api/firm/register-push-token` | `401` ✅ | — (POST, expected) | **ABSENT** ✅ | Empty | ✅ |

**Full header dump (example — `/api/firm/me`):**
```
HTTP/2 401
content-length: 0
www-authenticate: Bearer
server: Kestrel
```
No `location:` header. No redirect to `/auth/redirect-to-login`. Fix confirmed.

**vs. Previous failing behavior (firm-web:135):**
```
HTTP/2 401
content-length: 0
location: http://firm.dev.fortressam.ai/auth/redirect-to-login?ReturnUrl=%2Fapi%2Ffirm%2Fme
www-authenticate: Bearer
server: Kestrel
```

**Note on body:** Body is `content-length: 0` (empty), not `{"title":"Unauthorized","status":401}` JSON. The WI acceptance criteria stated "no `Location:` redirect header" — that is fully met. The JSON body was "or similar" in the task brief. This is a clean 401 that mobile clients can handle correctly. Not raising as a defect.

### Step 3 — AC3: Blazor Routes Still Redirect ✅ PASS

Cookie redirect for non-API paths is intact.

| Route | HTTP Status | Location | AC3 |
|-------|-------------|----------|-----|
| `GET /` | `302` ✅ | `→ /auth/redirect-to-login?ReturnUrl=%2F` ✅ | ✅ |
| `GET /meetings` | `302` ✅ | `→ /auth/redirect-to-login?ReturnUrl=%2Fmeetings` ✅ | ✅ |

Cookie authentication unchanged. Blazor routes redirect as before.

### Step 4 — AC4: No JWT Startup Errors ✅ PASS

CloudWatch log stream for task `03b032ab65cd4c61b5576c1a5e6b3d0f` (firm-web:136).

| Check | Result |
|-------|--------|
| ERROR-level log events (last 2h) | **None** ✅ |
| JwtBearer-specific errors | **None** ✅ |
| Database initialization | Complete (all idempotent migrations — expected `fail:` lines are schema guards) ✅ |
| App startup | `Application started. Press Ctrl+C to shut down.` ✅ |
| Listening on | `http://[::]:8080` ✅ |

The `fail: Microsoft.EntityFrameworkCore.Database.Command[20102]` log lines are idempotent migration guards — columns already exist, logged as expected, not real errors. Confirmed same pattern as every prior deployment.

### Step 5 — AC2: Live Bearer Token Test ⛔ HUMAN GATE

Testing with a real Entra Bearer token (PKCE flow from mobile app) requires a live Entra-issued JWT. Not automatable from this environment.

**Status:** Fred or Rob must test `GET /api/firm/me` with `Authorization: Bearer <token>` and confirm `200` response.

---

## Acceptance Criteria Summary

| AC | Description | Result |
|----|-------------|--------|
| AC1 | Mobile endpoints return `401` with no `Location:` header for unauthenticated requests | ✅ **PASS** — `Location` header gone on all 4 endpoints |
| AC2 | JWT Bearer live — accepts real Entra Bearer token | ⛔ **HUMAN GATE** — requires live Entra token |
| AC3 | FIRM web UI (Blazor, cookie auth) unaffected | ✅ **PASS** — `302 → /auth/redirect-to-login` intact |
| AC4 | No startup errors related to JWT Bearer configuration | ✅ **PASS** — Clean startup, zero errors |

---

## Issues Found

None. The fix resolves the AC1 failure from the previous QA (firm-web:135). No regressions introduced.

---

## Verdict Detail

**PASS** — All automatable acceptance criteria met. The `OnRedirectToLogin` suppression in `AddCookie` works exactly as specified: `/api/*` requests now get a clean `401` with no redirect. Cookie auth for Blazor routes is unaffected. ECS HEALTHY on `:136`. No startup errors.

**Remaining gate:** AC2 (live Bearer token) is a human gate — Fred or Rob must validate after deploying to a device.

---

## Test Duration
~10 minutes (2026-05-27 22:00–22:09 EDT)
