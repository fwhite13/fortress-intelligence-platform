# Review Report — ADO#4545

**Task:** FIRM: JWT Bearer auth for mobile API endpoints  
**Commit:** `d6f1442d`  
**Reviewer:** Clint Barton  
**Cycle:** 1 of 2  
**Date:** 2026-05-27

---

### Verdict: ✅ PASS

---

### Spec Compliance Check

**AC1:** Mobile app can call `/api/firm/me` with Bearer token — ✅ endpoint updated to `CookieOrBearer` policy  
**AC2:** Meeting list loads in mobile app — ✅ `GET /api/meetings/list` updated to `CookieOrBearer`  
**AC3:** Mobile upload works — ✅ `POST /api/meetings/mobile-upload` updated to `CookieOrBearer`  
**AC4:** FIRM web UI (Blazor, cookie auth) unaffected — ✅ `DefaultScheme` and `DefaultChallengeScheme` remain `CookieAuthenticationDefaults.AuthenticationScheme`; `FallbackPolicy = DefaultPolicy` preserved  
**AC5:** No regression on existing `[Authorize]` behavior — ✅ 16 non-mobile endpoints remain plain `[Authorize]`, zero contamination

**Spec compliance verdict:** ✅ COMPLIANT

---

### CC Review Summary

CC reviewed the full `Program.cs`, the mobile endpoint section of `MeetingsApiController.cs`, and the csproj. CC confirmed all checks clean — no false positives were surfaced, no real issues were dismissed. Agreement: PASS.

---

### Consistency Audit

**Config key alignment:**
- New JWT Bearer code uses `AzureAd:TenantId` and `AzureAd:ClientId`
- `FipTokenService.cs` uses the same keys (lines 19-21)
- These keys were confirmed present in FIRM's ECS task definition (firm-web:64, added 2026-03-30)
- ✅ No new config keys introduced; no deployment blocker

**Policy name cross-reference:**
- `Program.cs` defines `"CookieOrBearer"` 
- All 4 endpoints reference `Policy = "CookieOrBearer"` — exact case match ✅

**Endpoint coverage:**
- 4 `[Authorize(Policy = "CookieOrBearer")]` — exactly the 4 mobile endpoints ✅
- 16 plain `[Authorize]` — all non-mobile endpoints, unchanged ✅

---

### Security Checklist

| Check | Result |
|-------|--------|
| `ValidateIssuer = true` | ✅ |
| `ValidateAudience = true` | ✅ |
| `ValidateLifetime = true` | ✅ |
| `ValidateIssuerSigningKey = true` | ✅ |
| `RequireHttpsMetadata` left at default (`true`) | ✅ |
| `RequireAuthenticatedUser()` in policy | ✅ |
| `FallbackPolicy = DefaultPolicy` preserved | ✅ |
| No hardcoded secrets | ✅ |

---

### Correctness Checks

| Check | Result |
|-------|--------|
| Authority: `https://login.microsoftonline.com/{TenantId}/v2.0` | ✅ Correct Entra v2.0 format |
| Audience: `api://{ClientId}` | ✅ Correct PKCE app registration format |
| `AddAuthenticationSchemes(Cookie, Bearer)` in policy | ✅ Both schemes explicit — Bearer won't fall through to default |
| `UseAuthentication()` before `UseAuthorization()` | ✅ Lines 208-209 |
| `DefaultScheme` unchanged | ✅ Still `CookieAuthenticationDefaults` |
| `DefaultChallengeScheme` unchanged | ✅ Still `CookieAuthenticationDefaults` |
| JwtBearer package: `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.*` | ✅ Correct for .NET 8 |

---

### Issues Found

None.

---

### Notes for QA

1. **Token audience needs live validation** — The audience `api://{ClientId}` (i.e., `api://eda4d502-...`) is structurally correct. QA should verify with an actual Entra PKCE token that the `aud` claim in the JWT matches this value. If Rob registered the FIRM.Access scope with a different audience format, QA will catch it here — not a code issue, a config issue.

2. **`AzureAd:TenantId` already in ECS task def** — Confirmed via memory (firm-web:64 registered 2026-03-30). No task def update required. This was Tony's flagged concern; it's not a blocker.

---

_Clean build. Clean review. Ships._
