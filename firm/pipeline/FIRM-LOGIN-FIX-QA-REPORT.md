# QA Report: FIRM Login Fix

**Date:** 2026-03-11
**Commit:** `7f7dc32`
**Deployment:** FIRM firm-web:3 / FAIT fred-dev:63
**Verdict:** ❌ FAIL — Infrastructure blocker

---

## Summary

The three code changes (shared cookie domain, returnUrl flow, resolve-user X-Firm-Secret auth) cannot be tested because the Cognito app client is missing `meetings.dev.fortressam.ai` callback URL registration. The ALB auth layer returns `redirect_mismatch` on every request before any app code runs.

---

## Check Results

| Check | Result | Notes |
|-------|--------|-------|
| 1. FIRM landing page | ❌ FAIL | ALB Cognito auth returns redirect_mismatch — AWS error page shown |
| 2. Sign In redirects to FAIT | ⏭ SKIP | Blocked by Check 1 |
| 3. Full login flow | ⚠️ PARTIAL | FAIT `/auth/firm-callback` confirmed working; FIRM unreachable |
| 4. FIRM meetings page | ⏭ SKIP | Blocked by Check 1 |

---

## Confirmed Working (FAIT side)

- ✅ FAIT login page renders and accepts credentials
- ✅ `GET /auth/firm-callback?returnUrl=https://meetings.dev.fortressam.ai/auth/firm-session` → 302 redirect to FIRM correctly
- ✅ `GET /api/firm/resolve-user` (without secret) → 401 as expected
- ✅ FAIT fred-dev:63 HEALTHY

---

## Blocker: Cognito App Client Missing Callback URLs

**App client:** `fortress-tools-portal` (`e3ra6bg1oqji3i1mn2e7g1o1g`)
**Pool:** `us-east-1_CloTcONs1`

Required additions:

**Callback URLs:**
- `https://meetings.dev.fortressam.ai/oauth2/idpresponse`
- `https://meetings.dev.fortressam.ai/signin-oidc`

**Sign-out URLs:**
- `https://meetings.dev.fortressam.ai/signout-callback-oidc`

**Owner:** Fred (AWS Console change)

---

## Next Steps

1. Fred adds Cognito callback URLs (5 min AWS Console change)
2. Maria re-runs QA immediately — no new build/deploy needed
3. On PASS: proceed to completion report
