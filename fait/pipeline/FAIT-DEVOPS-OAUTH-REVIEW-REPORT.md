# Code Review Report: FAIT Azure DevOps OAuth Integration

**Reviewer:** Hawkeye (Clint Barton) — Code Reviewer  
**Commit:** `7c1b7bc`  
**Review Cycle:** 1 of 2  
**Date:** 2026-03-12  

---

## Verdict: NEEDS-CHANGES

Two blocking issues found (items #6 and #30). All other checklist items pass. Fix is straightforward — no architectural changes needed.

---

## Checklist Results

### Model + DB

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 1 | `UserDevOpsToken` has all required fields | ✅ PASS | `UserId`, `AccessToken`, `RefreshToken?`, `ExpiresAt`, `Email?`, `DisplayName?`, `ConnectedAt` — all present |
| 2 | `AppDbContext` has `UserDevOpsTokens` DbSet + `OnModelCreating` config → `user_devops_tokens` with snake_case columns | ✅ PASS | DbSet present; all 7 columns mapped with correct snake_case names |
| 3 | `CREATE TABLE IF NOT EXISTS` (idempotent) | ✅ PASS | DDL in `DatabaseInitializationService` uses `IF NOT EXISTS` |
| 4 | FK references `AspNetUsers(Id)` with `ON DELETE CASCADE` | ✅ PASS | `CONSTRAINT fk_devops_tokens_user FOREIGN KEY (user_id) REFERENCES AspNetUsers (Id) ON DELETE CASCADE` — however see issue #6 below |
| 5 | `PRIMARY KEY (user_id)` — one token row per user | ✅ PASS | `PRIMARY KEY (user_id)` in DDL |
| 6 | `user_id` column type matches `AspNetUsers.Id` | ❌ **FAIL** | See Critical Issue #1 below |

### DevOpsTokenService

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 7 | Config keys use `AzureDevOps:` prefix (not `Azure:`) | ✅ PASS | `AzureDevOps:ClientId`, `AzureDevOps:ClientSecret`, `AzureDevOps:TenantId` |
| 8 | `IsConfigured` is `false` when any key is empty | ✅ PASS | Three-way `&&` check in constructor; logs Warning and degrades gracefully |
| 9 | Auth URL correct format | ✅ PASS | `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize` with all required params |
| 10 | Scopes include `vso.work` and `offline_access` | ✅ PASS | `vso.work`, `vso.code`, `vso.build_execute`, `offline_access` — superset of minimum |
| 11 | Token exchange POST to correct endpoint with correct fields | ✅ PASS | All 6 required form fields present |
| 12 | Calls VSSPS profile API with Bearer token | ✅ PASS | `https://app.vssps.visualstudio.com/_apis/profile/me?api-version=7.1` with `Authorization: Bearer` |
| 13 | VSSPS profile failure is non-fatal | ✅ PASS | Profile fetch wrapped in `try/catch`; token stored regardless of outcome |
| 14 | `GetTokenAsync` returns `null` when no row exists | ✅ PASS | `FindAsync` returns `null` naturally |
| 15 | `DeleteTokenAsync` safe when no row exists | ✅ PASS | Null-checks before remove |
| 16 | `ExpiresAt` calculated from `expires_in` seconds | ✅ PASS | `DateTime.UtcNow.AddSeconds(expiresIn)` |

### OAuth Callback

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 17 | Reads `code` and `state` from query params | ✅ PASS | Both read from `ctx.Request.Query` |
| 18 | State validated against `IMemoryCache` key `"devops_oauth_state:{state}"` | ✅ PASS | Exact key format used |
| 19 | State removed from cache after use | ✅ PASS | `memoryCache.Remove(cacheKey)` called before processing |
| 20 | UserId from `HttpContext.User` claims | ✅ PASS | `ctx.User.FindFirst(ClaimTypes.NameIdentifier)` |
| 21 | Success redirects to `/settings?devops_connected=true` | ✅ PASS | |
| 22 | Failure redirects to `/settings?devops_error=<reason>` | ✅ PASS | All failure paths redirect; no unhandled exceptions thrown |

### Settings.razor

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 23 | `DevOpsTokenService` injected and used | ✅ PASS | `@inject DevOpsTokenService DevOpsTokenSvc` |
| 24 | `!IsConfigured` shows info alert, no Connect button | ✅ PASS | `if (!DevOpsTokenSvc.IsConfigured)` renders alert only |
| 25 | Connected: shows display name/email and Disconnect button | ✅ PASS | Shows `_devOpsDisplayName ?? "Azure DevOps user"` with Disconnect button |
| 26 | Disconnected and configured: shows Connect button only | ✅ PASS | Else branch renders Connect button only |
| 27 | Query params handled in `OnParametersSetAsync` | ✅ PASS | Both `devops_connected` and `devops_error` handled with snackbar feedback |

### Security

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 28 | `state` is cryptographically random | ✅ PASS | `Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))` — 256 bits of entropy |
| 29 | Access token NOT logged at Info level or above | ✅ PASS | Line 155 logs `displayName`, `email`, `expiry` only — no token value |
| 30 | Redirect URI not user-supplied | ❌ **FAIL** | See Critical Issue #2 below |

---

## Critical Issues (Blocking)

### Issue #1 — FK References Wrong Table Name (`AspNetUsers` vs `users`)

**Severity:** Critical — will cause a runtime FK constraint error on first deploy

**File:** `src/FortressAI.Web/Services/DatabaseInitializationService.cs`, line 113  
**File:** `src/FortressAI.Shared/Models/UserDevOpsToken.cs`, line 5

**Problem:**

The raw SQL DDL for `user_devops_tokens` has:
```sql
CONSTRAINT fk_devops_tokens_user FOREIGN KEY (user_id) REFERENCES AspNetUsers (Id) ON DELETE CASCADE
```

But FAIT does **not** use ASP.NET Identity — the actual users table is `users` (mapped in `AppDbContext.OnModelCreating` via `entity.ToTable("users")`). There is no `AspNetUsers` table in this database. Additionally, the `AppUser.Id` is `Guid` (C#) stored as `CHAR(36)`, but the column type is correct — the **table name** is the bug.

Every other table in this codebase that FKs to users references `users(Id)` directly (e.g., `user_microsoft_tokens` has no explicit FK in DDL at all — EF handles it). The `chat_attachments` DDL FKs `conversations(Id)` directly. The `AspNetUsers` reference here is a copy-paste error from a stock ASP.NET Identity template that doesn't match FAIT's schema.

**On deploy:** MySQL will reject the `CREATE TABLE` statement with `errno: 150 "Foreign key constraint is incorrectly formed"` because `AspNetUsers` doesn't exist. The `DatabaseInitializationService` catches the exception and logs a warning (non-fatal), so the table will silently **not be created** — breaking all DevOps OAuth functionality at runtime.

**Fix:**
```sql
-- Line 113: Change
CONSTRAINT fk_devops_tokens_user FOREIGN KEY (user_id) REFERENCES AspNetUsers (Id) ON DELETE CASCADE

-- To:
CONSTRAINT fk_devops_tokens_user FOREIGN KEY (user_id) REFERENCES users (Id) ON DELETE CASCADE
```

---

### Issue #2 — Redirect URI Built from `ctx.Request.Host` (Host Header Injection Risk)

**Severity:** Critical — security vulnerability

**File:** `src/FortressAI.Web/Program.cs`, line 402

**Problem:**

In the `/auth/devops-callback` handler, the redirect URI passed to `ExchangeCodeAsync` is constructed dynamically from the incoming request:

```csharp
var redirectUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}/auth/devops-callback";
```

`ctx.Request.Host` is derived from the HTTP `Host` header, which can be manipulated by an attacker in certain proxy/load-balancer configurations. While Azure validates the redirect URI against the registered callback, the reconstructed URI is also passed to the token exchange request. If an attacker can influence `Host` to a value that Azure has registered (unlikely but possible in misconfigured environments), they could redirect the token exchange response.

More concretely: if this URI doesn't match what was registered in Azure AD exactly, token exchange will silently fail. Relying on the dynamic Host is fragile and unnecessary.

The correct pattern (already used elsewhere in FAIT, e.g. `AzureDevOps:RedirectUri` is already a config key read in `DatabaseInitializationService`) is to read from config.

**Fix:**
```csharp
// Program.cs, replace line 402:
var redirectUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}/auth/devops-callback";

// With:
var redirectUri = config["AzureDevOps:RedirectUri"]
    ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}/auth/devops-callback";  // fallback only
```

The config key `AzureDevOps:RedirectUri` is already used in `DatabaseInitializationService` (line ~300 of that file) when seeding the MCP server record — use the same key here for consistency.

---

## Non-Blocking Observations (Nitpicks — Fix Anyway)

### N1 — `DevOpsTokenService` Instantiated with `new` in Callback (Minor)

**File:** `Program.cs`, lines 400–401

```csharp
var devOpsTokenService = new DevOpsTokenService(dbFactory, logger, config, httpFactory);
```

The service is registered as `AddScoped<DevOpsTokenService>()` at line 71 — but the callback creates a new instance manually rather than resolving from DI. This works correctly (all deps are available), but it bypasses the DI lifetime and is inconsistent. 

**Preferred pattern:**
```csharp
var devOpsTokenService = ctx.RequestServices.GetRequiredService<DevOpsTokenService>();
```

Note: this requires checking `IsConfigured` is already true at call time (it will be, since the user got a valid auth code back from Azure, which requires `IsConfigured` to have been true when `ConnectDevOps()` was called). No behavioral change — just cleaner.

### N2 — `OnParametersSetAsync` Used for Query Param Feedback Instead of `OnAfterRenderAsync`

**File:** `Settings.razor`, lines 378–404

Snackbar feedback for `devops_connected`/`devops_error` is triggered in `OnParametersSetAsync`. In Blazor Server, this can sometimes fire before the component is fully rendered, resulting in snackbars that appear before the page is visible. The M365 callback uses the same pattern, so this is consistent — but worth flagging as a known Blazor timing quirk. Not a bug; cosmetic only.

### N3 — `error_description` Not URL-Decoded Before Display

**File:** `Program.cs`, line 380

```csharp
return Results.Redirect($"/settings?devops_error={Uri.EscapeDataString($"{error}: {errorDesc}")}");
```

`errorDesc` comes from `ctx.Request.Query["error_description"].ToString()` which Azure returns URL-encoded. It gets double-encoded here (`EscapeDataString` of an already-encoded string). The snackbar will show something like `access_denied%3A+The+user+cancelled...` instead of the decoded message. Not a security issue, just ugly UX.

**Fix:** Decode before re-encoding:
```csharp
var errorDesc = Uri.UnescapeDataString(ctx.Request.Query["error_description"].ToString());
```

---

## Summary

| Category | Pass | Fail | Total |
|----------|------|------|-------|
| Model + DB | 5 | 1 | 6 |
| DevOpsTokenService | 10 | 0 | 10 |
| OAuth Callback | 6 | 0 | 6 |
| Settings.razor | 5 | 0 | 5 |
| Security | 2 | 1 | 3 |
| **Total** | **28** | **2** | **30** |

**Two blockers must be fixed before merge:**
1. FK references `AspNetUsers` — doesn't exist in FAIT schema; should be `users`
2. Redirect URI from `ctx.Request.Host` — use config key `AzureDevOps:RedirectUri` instead

Both are one-line fixes. No architectural changes required. Resubmit for cycle 2.

---

*— Hawkeye*
