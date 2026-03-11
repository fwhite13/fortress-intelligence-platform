# FIRM Login Fix — Code Review Report

**Commit:** `249d143`
**Review Cycle:** 1 of 2
**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-03-11

---

## Verdict: NEEDS-CHANGES

Two issues must be fixed before this ships. Neither is catastrophic, but item #5 is a correctness
bug and item #8 exposes a subtle open-redirect risk. All other items pass.

---

## Checklist Results

### Cookie Sharing (items 1–6)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | FAIT `AddCookie`: `Cookie.Name = ".FortressAI.Session"` set explicitly | ✅ PASS | Line: `options.Cookie.Name = ".FortressAI.Session";` |
| 2 | FAIT `AddCookie`: `Cookie.Domain` from `Auth:CookieDomain` config, empty-string default | ✅ PASS | `options.Cookie.Domain = builder.Configuration["Auth:CookieDomain"] ?? "";` |
| 3 | FAIT `AddCookie`: `SameSite = Lax` and `SecurePolicy = Always` | ✅ PASS | Both set correctly |
| 4 | FIRM `AddCookie`: `Cookie.Name = ".FortressAI.Session"` — matches FAIT exactly | ✅ PASS | Matches |
| 5 | FIRM `AddCookie`: `Cookie.Domain` from config — same pattern as FAIT | ⚠️ **FAIL** | See Critical Issue #1 below |
| 6 | FIRM `AddCookie`: `SameSite = Lax` + `SecurePolicy = Always` | ✅ PASS | Both set correctly |

### returnUrl Flow (items 7–12)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 7 | FIRM `/auth/redirect-to-login`: constructs redirect with `returnUrl` pointing to firm-session (from config) | ✅ PASS | Uses `FIP:FirmCallbackUrl` with fallback to `https://meetings.dev.fortressam.ai/auth/firm-session` |
| 8 | FIRM `Login.razor`: sign-in link goes through `/auth/firm-callback?returnUrl=` | ✅ PASS | `_faitLoginUrl` constructed correctly in `OnInitializedAsync` |
| 9 | FAIT `/auth/firm-callback`: validates `returnUrl` domain | ⚠️ **FAIL** | See Important Issue #2 below |
| 10 | FAIT `/auth/firm-callback`: missing/invalid `returnUrl` → redirects to `/` (not crash) | ✅ PASS | Falls through to `ctx.Response.Redirect("/")` |
| 11 | FIRM `/auth/firm-session`: calls `AuthenticateAsync` to verify shared cookie | ✅ PASS | `await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)` |
| 12 | FIRM `/auth/firm-session`: invalid/missing cookie → redirects to `/` (not crash) | ✅ PASS | `if (!authResult.Succeeded) { ctx.Response.Redirect("/"); return; }` |

### resolve-user Auth Fix (items 13–17)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 13 | FAIT `ResolveUser`: loopback IP check completely removed | ✅ PASS | No loopback/RemoteIpAddress check anywhere in the method |
| 14 | FAIT `ResolveUser`: uses `X-Firm-Secret` header auth | ✅ PASS | Same pattern as `MeetingComplete` |
| 15 | FAIT `ResolveUser`: returns 401 if secret missing or wrong | ✅ PASS | `return Unauthorized(...)` |
| 16 | FIRM `/auth/firm-session`: calls `GET /api/firm/resolve-user?entraOid=...` with `X-Firm-Secret` header | ✅ PASS | `httpClient.DefaultRequestHeaders.Add("X-Firm-Secret", sharedSecret)` |
| 17 | FIRM `/auth/firm-session`: resolve-user failure is non-fatal | ✅ PASS | Wrapped in `try/catch`, logs warning, continues to `SaveChangesAsync` and `Redirect("/meetings")` |

### FirmUser Upsert (items 18–20)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 18 | FIRM `/auth/firm-session`: upserts `FirmUser` by email | ✅ PASS | `FirstOrDefaultAsync(u => u.Email == email)` → creates if null, updates `LastLoginAt` if exists |
| 19 | resolve-user call only if `FaitUserId` null/empty AND `entraOid` non-null | ✅ PASS | `if (string.IsNullOrEmpty(firmUser.FaitUserId) && !string.IsNullOrEmpty(entraOid))` |
| 20 | Single `db.SaveChangesAsync()` after both upsert and FaitUserId update | ✅ PASS | One `await db.SaveChangesAsync()` at the end of the email block, after the resolve-user try/catch |

### Regression Safety (items 21–22)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 21 | FAIT `/api/firm/meeting-complete` unchanged — still uses `X-Firm-Secret` + `Firm:SharedSecret` | ✅ PASS | `MeetingComplete` method untouched; same secret check pattern |
| 22 | FAIT email/password login flow unaffected by `AddCookie` changes | ✅ PASS | `AddCookie` options change is additive (name, domain, SameSite, SecurePolicy only). Existing Cognito sessions will be re-issued on next login with the new domain/name. |

---

## Issues

### 🔴 Critical — Issue #1: FIRM `Cookie.Domain` conditionally set (item #5)

**File:** `src/FortressIntelligenceRM.Web/Program.cs`

**Current code:**
```csharp
var cookieDomain = builder.Configuration["Auth:CookieDomain"] ?? "";
if (!string.IsNullOrEmpty(cookieDomain))
    options.Cookie.Domain = cookieDomain;
```

**FAIT's code (the correct pattern):**
```csharp
options.Cookie.Domain = builder.Configuration["Auth:CookieDomain"] ?? "";
```

**Problem:** FIRM's version only sets `Cookie.Domain` when the config value is non-empty. FAIT
unconditionally assigns it (defaulting to `""`). The behavior difference matters:

- In production, `Auth:CookieDomain` = `.dev.fortressam.ai` → both behave the same.
- In **dev/local** where `Auth:CookieDomain` is absent, FAIT sets `Cookie.Domain = ""` (browser
  uses request host, which is correct). FIRM leaves `Cookie.Domain` at its ASP.NET default, which
  may differ and can prevent the cookie from being read.

The real issue is **inconsistency with FAIT**. The whole point of this fix is that both apps have
identical cookie configuration. FIRM must match FAIT's pattern exactly.

**Fix:**
```csharp
// Remove the if-guard. Assign unconditionally, same as FAIT:
options.Cookie.Domain = builder.Configuration["Auth:CookieDomain"] ?? "";
```

---

### 🟡 Important — Issue #2: `returnUrl` domain validation uses `EndsWith` without dot-anchor (item #9)

**File:** `src/FortressAI.Web/Program.cs`

**Current code:**
```csharp
uri.Host.EndsWith(".fortressam.ai", StringComparison.OrdinalIgnoreCase) ||
uri.Host.Equals("fortressam.ai", StringComparison.OrdinalIgnoreCase)
```

**Problem:** `EndsWith(".fortressam.ai")` correctly requires the dot prefix, so
`evilfortressam.ai` won't pass. That part is fine. However, the check passes for any host that
ends with `.fortressam.ai` — including contrived ones like `evil.notfortressam.ai`... wait, no,
that doesn't end with `.fortressam.ai`. Actually the dot-anchored `EndsWith` is sound.

The real exposure is different: **the check does not verify that `returnUrl` uses `https`**.
A URL like `http://firm.fortressam.ai/...` passes domain validation and gets a redirect issued.
Over HTTP, the shared auth cookie (marked `SecurePolicy = Always`) won't be sent, but the user
will land on an insecure page. More critically, if any dev/staging environment is still on HTTP,
an attacker on the network can intercept the redirect.

**Fix:** Add scheme validation alongside the domain check:

```csharp
if (!string.IsNullOrEmpty(returnUrl) &&
    Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) &&
    uri.Scheme == Uri.UriSchemeHttps &&                          // ← add this
    (uri.Host.EndsWith(".fortressam.ai", StringComparison.OrdinalIgnoreCase) ||
     uri.Host.Equals("fortressam.ai", StringComparison.OrdinalIgnoreCase)))
```

This is a one-line change and eliminates the HTTP redirect vector entirely.

---

## Nitpicks (non-blocking)

### N1: `FirmIntegrationController.ResolveUser` stale XML doc comment

**File:** `src/FortressAI.Web/Controllers/FirmIntegrationController.cs`

The `<summary>` XML doc still reads:
```
/// Restricted to loopback — called by FIRM on the same host during user login...
```

The loopback restriction is gone. The comment is now misleading. Update to reflect
X-Firm-Secret auth.

**Suggested replacement:**
```
/// GET /api/firm/resolve-user?entraOid={oid}
/// Returns FAIT internal user GUID for the given Entra OID.
/// Auth: X-Firm-Secret header (shared secret, same pattern as meeting-complete).
/// Called by FIRM during /auth/firm-session to populate firm_users.fait_user_id.
```

### N2: `Login.razor` — `_faitLoginUrl` default is root, not `/auth/firm-callback`

**File:** `src/FortressIntelligenceRM.Web/Components/Pages/Login.razor`

```csharp
private string _faitLoginUrl = "https://fait.dev.fortressam.ai/";
```

This field is always overwritten in `OnInitializedAsync` before render (because `_checked`
starts false and the sign-in button is hidden until `_checked = true`), so there's no functional
risk. But if a race or SSR edge case renders the template before `OnInitializedAsync` completes,
the button will link to the FAIT root instead of going through `/auth/firm-callback`. Initialize
the field to `#` or the correct default value:

```csharp
private string _faitLoginUrl = "#";
```

### N3: FIRM `Login.razor` — no loading/redirect feedback for already-authenticated users

When `authState.User.Identity?.IsAuthenticated == true`, the component calls
`Navigation.NavigateTo("/meetings")` but the page is blank (`_checked` remains false) for the
duration. Not a bug, but consider setting `_checked = true` before the early return so the
full-screen dark div doesn't flash. Minor UX polish.

---

## Focus Item Summary

| Focus | Item | Result |
|-------|------|--------|
| #9 — returnUrl domain validation bypassable? | Scheme check missing (HTTP allowed) | **Fix required** |
| #13 — loopback check fully removed? | ✅ Clean — no remnant IP code | Pass |
| #17 — resolve-user non-fatal? | ✅ Exception caught, logs warning, continues | Pass |
| #20 — single SaveChangesAsync? | ✅ One call at end of email block | Pass |

---

## Summary

The three root-cause fixes (cookie domain, returnUrl flow, X-Firm-Secret auth) are correctly
implemented. The logic is sound across all 22 checklist items except two:

1. **FIRM `Cookie.Domain` uses a conditional assignment** that diverges from FAIT's unconditional
   pattern — risk in dev and any environment without `Auth:CookieDomain` configured.
2. **FAIT `/auth/firm-callback` doesn't validate `https` scheme** — HTTP returnUrls to
   `*.fortressam.ai` are accepted.

Both fixes are one-liners. Send back to Tony, resolve, and this is a clean PASS on cycle 2.

---

*— Clint Barton / Hawkeye*
*Cycle 1 of 2 — NEEDS-CHANGES*
