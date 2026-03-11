# Build Report: FIRM Login Fix

**Task:** FIRM-LOGIN-FIX  
**Agent:** Tony Stark (software-engineer)  
**Date:** 2026-03-11  
**Commit:** 249d143  
**Branch:** main  

---

## Summary

Implemented cross-subdomain login flow for FIRM ↔ FAIT. All 5 root causes addressed across 7 file changes. Both services build clean.

---

## Changes Made

### Change 1 — FAIT `Program.cs`: Cookie domain + name
**File:** `~/projects/fip/fait/src/FortressAI.Web/Program.cs`  
**Root Cause:** A  
Updated `AddCookie` options block to set:
- `options.Cookie.Name = ".FortressAI.Session"` — explicit shared name
- `options.Cookie.Domain = builder.Configuration["Auth:CookieDomain"] ?? ""` — reads from config
- `options.Cookie.SameSite = SameSiteMode.Lax` — required for cross-subdomain redirect flows
- `options.Cookie.SecurePolicy = CookieSecurePolicy.Always` — HTTPS enforcement

### Change 2 — FAIT `Program.cs`: `/auth/firm-callback` endpoint
**File:** `~/projects/fip/fait/src/FortressAI.Web/Program.cs`  
**Root Cause:** C  
Added `GET /auth/firm-callback?returnUrl={url}` endpoint immediately after the health endpoint.  
- Validates returnUrl — only redirects to `*.fortressam.ai` or `fortressam.ai` domains
- Redirects to `/` (FAIT root) if returnUrl is missing or untrusted

### Change 3 — FAIT `Controllers/FirmIntegrationController.cs`: Fix resolve-user auth
**File:** `~/projects/fip/fait/src/FortressAI.Web/Controllers/FirmIntegrationController.cs`  
**Root Cause:** D  
Replaced loopback IP check (`IPAddress.IsLoopback`) with `X-Firm-Secret` header auth:
- Reads `Firm:SharedSecret` from config
- Returns `401 Unauthorized` if header is missing or doesn't match
- Removed `using System.Net;` (no longer needed)
- Pattern is identical to `meeting-complete` endpoint auth

### Change 4 — FIRM `Program.cs`: Cookie domain + name to match FAIT
**File:** `~/projects/fip/firm/src/FortressIntelligenceRM.Web/Program.cs`  
**Root Cause:** A  
Updated `AddCookie` options block to match FAIT exactly:
- `options.Cookie.Name = ".FortressAI.Session"` — must be identical to FAIT
- `options.Cookie.Domain = cookieDomain` (from `Auth:CookieDomain` config)
- `options.Cookie.SameSite = SameSiteMode.Lax`
- `options.Cookie.SecurePolicy = CookieSecurePolicy.Always`

### Change 5 — FIRM `Program.cs`: Fix `/auth/redirect-to-login` to include returnUrl
**File:** `~/projects/fip/firm/src/FortressIntelligenceRM.Web/Program.cs`  
**Root Cause:** B  
Updated `/auth/redirect-to-login` endpoint to build a redirect URL that includes `returnUrl`:
- Constructs: `{FIP:LoginUrl}/auth/firm-callback?returnUrl={FIP:FirmCallbackUrl}`
- Default `FIP:FirmCallbackUrl` = `https://meetings.dev.fortressam.ai/auth/firm-session`
- Uses `Uri.EscapeDataString` for safe encoding

### Change 6 — FIRM `Program.cs`: Add `/auth/firm-session` endpoint
**File:** `~/projects/fip/firm/src/FortressIntelligenceRM.Web/Program.cs`  
**Root Cause:** E  
Added `GET /auth/firm-session` — the landing endpoint FAIT redirects to after login:
1. Authenticates via shared cookie (`CookieAuthenticationDefaults.AuthenticationScheme`)
2. Redirects to `/` if cookie invalid
3. Upserts `FirmUser` record (creates if new, updates `LastLoginAt` if existing)
4. If `FaitUserId` is null, calls `GET {FIP:FaitApiUrl}/api/firm/resolve-user?entraOid={oid}` with `X-Firm-Secret` header
5. Stores resolved `FaitUserId` in `firm_users.fait_user_id`
6. Redirects to `/meetings` on success

DI dependencies used (both already registered):
- `IDbContextFactory<FirmDbContext>` — registered via `AddDbContextFactory`
- `IHttpClientFactory` — registered via `AddHttpClient`

### Change 7 — FIRM `Components/Pages/Login.razor`: Add returnUrl to Sign-in link
**File:** `~/projects/fip/firm/src/FortressIntelligenceRM.Web/Components/Pages/Login.razor`  
**Root Cause:** B  
Updated `OnInitializedAsync` to build the FAIT login URL with returnUrl:
- Constructs: `{FIP:LoginUrl}/auth/firm-callback?returnUrl={FIP:FirmCallbackUrl}`
- Default `FIP:FirmCallbackUrl` = `https://meetings.dev.fortressam.ai/auth/firm-session`
- Consistent with Change 5 (same URL pattern)

---

## Build Results

| Project | Errors | Warnings | Result |
|---------|--------|----------|--------|
| `FortressAI.Web` (FAIT) | 0 | 32 (pre-existing) | ✅ PASS |
| `FortressIntelligenceRM.Web` (FIRM) | 0 | 0 | ✅ PASS |

Warnings in FAIT are all pre-existing (nullable reference, MUD0002 Blazor component attributes, Bedrock model ID pattern). None introduced by this change.

---

## Generated Shared Secret

```
Firm__SharedSecret=5bb8ff8088fdac0f8fb19319723fae663124db1ffee73e5e6de0b64e67ac606e
```

> ⚠️ **For Rhodey's ECS deploy only — do NOT commit to repo.**

---

## ECS Environment Variables Required

### FIRM Task Definition
| Variable | Value |
|----------|-------|
| `Auth__CookieDomain` | `.dev.fortressam.ai` |
| `FIP__FaitApiUrl` | `https://fait.dev.fortressam.ai` |
| `FIP__FirmCallbackUrl` | `https://meetings.dev.fortressam.ai/auth/firm-session` |
| `Firm__SharedSecret` | `5bb8ff8088fdac0f8fb19319723fae663124db1ffee73e5e6de0b64e67ac606e` |

### FAIT Task Definition
| Variable | Value |
|----------|-------|
| `Auth__CookieDomain` | `.dev.fortressam.ai` |
| `Firm__SharedSecret` | `5bb8ff8088fdac0f8fb19319723fae663124db1ffee73e5e6de0b64e67ac606e` |

> Both task defs must use the **same** `Firm__SharedSecret` value.

---

## Login Flow After Deploy

```
User hits FIRM (unauthenticated)
  → /auth/redirect-to-login
  → FAIT /auth/firm-callback?returnUrl=https://meetings.dev.fortressam.ai/auth/firm-session
  → FAIT OIDC login (Entra)
  → FAIT sets .FortressAI.Session cookie on .dev.fortressam.ai
  → FAIT /auth/firm-callback validates returnUrl → redirects to FIRM
  → FIRM /auth/firm-session reads shared cookie
  → FIRM upserts FirmUser, calls FAIT /api/firm/resolve-user (X-Firm-Secret)
  → FIRM stores fait_user_id in firm_users
  → Redirect to /meetings ✅
```

---

## Self-Review Checklist

- [x] All 7 changes implemented per spec
- [x] Cookie name `.FortressAI.Session` matches exactly in both apps
- [x] Cookie domain `.dev.fortressam.ai` driven by config in both apps
- [x] `/auth/firm-callback` validates returnUrl domain before redirecting
- [x] `resolve-user` loopback check fully replaced with X-Firm-Secret
- [x] `/auth/firm-session` handles unauthenticated case (redirects to `/`)
- [x] `FirmUser` upsert handles both new and existing users
- [x] `FaitUserId` resolve is best-effort (logs warning, doesn't block login)
- [x] Both `IDbContextFactory` and `IHttpClientFactory` confirmed registered
- [x] `Firm__SharedSecret` generated and included for Rhodey
- [x] Secret NOT committed to repo
- [x] FAIT build: 0 errors ✅
- [x] FIRM build: 0 errors ✅
- [x] Committed and pushed: `249d143`
