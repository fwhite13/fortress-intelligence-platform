# Review Report: FIRM-CORE-ACTIVATION

**Reviewer:** Hawkeye  
**Date:** 2026-03-08  
**Repo:** `fortress-intelligence-rm`  
**Build Report:** `pipeline/FIRM-CORE-BUILD-REPORT.md`  
**Spec:** `memory/projects/firm-core-activation-spec.md`

---

### Verdict: NEEDS-CHANGES

Two important issues (C1: cookie domain, C2: callback secret bypass) require fixes before this goes to production. Everything else is solid — this sprint was well-executed.

---

## Consistency Audit

**Files Cross-Referenced:**
- `Program.cs` ↔ FAIT auth config keys — ✅ All match exactly (`Auth:EntraAuthority`, `Auth:EntraClientId`, `Auth:EntraClientSecret`)
- `Program.cs` ↔ FAIT ApplicationName — ✅ `SetApplicationName("FortressAI")` — exact match
- `Program.cs` ↔ `FirmDbContext.cs` — ✅ `PersistKeysToDbContext<FirmDbContext>()` wired correctly
- `MeetingsApiController.cs` `X-Bot-Secret` ↔ `meeting-bot.ts` `X-Bot-Secret` header — ✅ Header name matches exactly
- `VpBotService.cs` ECS env vars ↔ `meeting-bot.ts` env var names — ✅ `FIRM_API_URL`, `MEETING_ID`, `MEETING_URL`, `BOT_DISPLAY_NAME`, `BOT_CALLBACK_SECRET` all match
- `DatabaseInitializationService.cs` extraTables ↔ `FirmDbContext.cs` entities — ✅ All 5 `firm_*` tables + `firm_data_protection_keys` present in both
- `FirmMeeting.Status` ↔ `OnModelCreating` — ✅ `HasConversion<string>()` present

**Undocumented dependencies checked:**
- `MeetingDetail.razor` calls `GetOrCreateUserAsync` — user auto-provisioning happens at page load ✅ (but with a caveat; see I1)
- `/api/meetings/join` controller checks user existence via `GetOrCreateUserAsync` called from `Meetings.razor` before the join POST — the API itself does NOT call `GetOrCreateUserAsync` (see I1)

---

## Critical Issues — 0

No critical issues found. The two items below are Important (NEEDS-CHANGES) not Critical (FAIL).

---

## Important Issues — 2

### I1: Auth Cookie Domain Not Set — Cross-Subdomain Sharing Will NOT Work

- **File:** `Program.cs` (lines ~87–95, the `.AddCookie(...)` block)
- **Category:** correctness / auth
- **Issue:** `CookieAuthenticationOptions.Cookie.Domain` is not set. For `meetings.dev.fortressam.ai` to accept a cookie issued by `fait.dev.fortressam.ai` (and vice versa), the cookie domain must be `.fortressam.ai`. Without this, each app issues a host-bound cookie and the other app cannot read it — even with identical DataProtection keys and ApplicationName.
- **Evidence:**
  ```csharp
  .AddCookie(options =>
  {
      options.LoginPath = "/";
      options.AccessDeniedPath = "/access-denied";
      options.ExpireTimeSpan = TimeSpan.FromHours(8);
      options.SlidingExpiration = true;
      // ← Cookie.Domain NOT SET
  });
  ```
- **Impact:** Users authenticated on `fait.dev.fortressam.ai` will NOT be SSO'd into `meetings.dev.fortressam.ai`. They'll hit the login page every time instead of being silently passed through. The DataProtection + ApplicationName setup is correct; only the domain is missing.
- **Fix:**
  ```diff
  .AddCookie(options =>
  {
      options.LoginPath = "/";
      options.AccessDeniedPath = "/access-denied";
      options.ExpireTimeSpan = TimeSpan.FromHours(8);
      options.SlidingExpiration = true;
  +   options.Cookie.Domain = ".fortressam.ai";
  });
  ```
  Note: This value should ideally come from config so it can be `.localhost` in dev (or omitted). Consider:
  ```csharp
  var cookieDomain = builder.Configuration["Auth:CookieDomain"]; // ".fortressam.ai" in prod, null in local dev
  if (!string.IsNullOrEmpty(cookieDomain))
      options.Cookie.Domain = cookieDomain;
  ```

---

### I2: `/api/vp/callback` Secret Check Bypassed When `Firm:BotCallbackSecret` Not Configured

- **File:** `MeetingsApiController.cs` (lines 81–84)
- **Category:** security
- **Issue:** The secret check is `if (!string.IsNullOrEmpty(expectedSecret) && ...)` — meaning if `Firm:BotCallbackSecret` is not configured in the environment, **all callback requests pass through with no authentication**. In dev this is intentional, but if the secret is accidentally omitted from production config, anyone can POST fake meeting status updates.
- **Evidence:**
  ```csharp
  var expectedSecret = _config["Firm:BotCallbackSecret"];
  var providedSecret = Request.Headers["X-Bot-Secret"].FirstOrDefault();
  if (!string.IsNullOrEmpty(expectedSecret) && providedSecret != expectedSecret)
  {
      return Unauthorized();
  }
  ```
- **Impact:** If `Firm:BotCallbackSecret` is missing from ECS task config (easy to forget), malicious actors could POST fake `summary_complete` payloads to inject content into meeting summaries, or spam `recording_failed` to corrupt meeting status.
- **Fix Option A — Fail closed in production:**
  ```diff
  var expectedSecret = _config["Firm:BotCallbackSecret"];
  var providedSecret = Request.Headers["X-Bot-Secret"].FirstOrDefault();
  + if (string.IsNullOrEmpty(expectedSecret))
  + {
  +     _logger.LogWarning("FIRM: Firm:BotCallbackSecret not configured — callback endpoint is OPEN");
  +     // Allow through in dev only
  +     if (!app.Environment.IsDevelopment())
  +         return StatusCode(503, "Callback secret not configured");
  + }
  if (!string.IsNullOrEmpty(expectedSecret) && providedSecret != expectedSecret)
  {
      return Unauthorized();
  }
  ```
  **Fix Option B (simpler)** — add `Firm:BotCallbackSecret` to the required config validation at startup, so the app refuses to start in production without it. Either approach is acceptable. At minimum, a startup log warning (already present for other config) would be good.

---

## Nitpicks — 3

**N1:** `firm_users` auto-provisioning happens in Blazor pages (`Meetings.razor`, `MeetingDetail.razor`), not in `OnTokenValidated`. This means the `/api/meetings/join` endpoint has no guarantee the user record exists — if someone calls the API directly (e.g. with a valid cookie but before loading the UI), the `created_by` FK insert will fail. The fix is to also call `GetOrCreateUserAsync` at the start of `JoinMeetingAsync` in `MeetingsApiController`. Not blocking because the normal flow (UI → join dialog) always calls the Blazor page first.

**N2:** `Dockerfile.debian` runtime stage creates `/app/keys` (`mkdir -p /app/keys`) but DataProtection keys are stored in the DB (`PersistKeysToDbContext`), not on the filesystem. This is benign (the directory is harmless) but the `mkdir` is dead code — remove for clarity.

**N3:** Status update safety for `transcription_complete` retry scenario: transcript segments are inserted without a duplicate check — if the bot retries and fires `transcription_complete` again, you get doubled transcripts. The `summary_complete` path does check `firstOrDefault` before insert ✅, but `transcription_complete` does not. Low risk in practice (bots don't typically retry transcription), but worth noting. Could be addressed with a pre-clear (`RemoveRange`) similar to how participants are handled.

---

## Positive Observations

This is a well-structured sprint with minimal shortcuts. Specifically:

- ✅ `SetApplicationName("FortressAI")` — exactly right, matches FAIT
- ✅ Config key naming (`Auth:EntraAuthority`, `Auth:EntraClientId`, `Auth:EntraClientSecret`) — exact parity with FAIT
- ✅ `UseStubAuth` toggle present, defaults to `false` — production safe
- ✅ `PersistKeysToDbContext<FirmDbContext>()` — correct, no in-memory fallback
- ✅ All three OIDC events present and correct (http→https redirect fix, role mapping, PostLogoutRedirectUri)
- ✅ Cookie `LoginPath = "/"` — correct (not `/login`)
- ✅ `/auth/login` challenges OIDC, `/auth/logout` signs out both cookie + OIDC — correct dual signout
- ✅ `Login.razor` redirects to `/meetings` on authenticated access — correct
- ✅ `Login.razor` has NO `[Authorize]` attribute — correct
- ✅ All 5 entity classes with `firm_*` table names via `ToTable()` in `OnModelCreating`
- ✅ `FirmMeeting.Status` uses `HasConversion<string>()` — correct
- ✅ `FirmDbContext` implements `IDataProtectionKeyContext` with `DataProtectionKeys` DbSet mapped to `firm_data_protection_keys`
- ✅ Foreign keys wired: `CreatedBy → FirmUser.Id`, participants/transcripts/summary all cascade on delete
- ✅ `DatabaseInitializationService` uses `IRelationalDatabaseCreator.CreateTablesAsync()` in non-fatal inner try-catch ✅, followed by `extraTables` loop with per-statement catch for MySQL error 1060/1061 ✅
- ✅ All 6 tables present in `extraTables` (5 `firm_*` + `firm_data_protection_keys`)
- ✅ `MySqlConnectionStringBuilder` used — handles `=` in passwords correctly
- ✅ `POST /api/meetings/join` creates `FirmMeeting` with `MeetingStatus.Joining`, calls `TriggerBotAsync`, returns `{ meetingId }` ✅
- ✅ `POST /api/vp/callback` validates `X-Bot-Secret` (conditionally), updates status, writes transcript on `transcription_complete`, writes summary on `summary_complete`, writes participants on `recording` ✅
- ✅ Download endpoints require auth AND ownership check (`created_by == current user id`) ✅
- ✅ `/api/meetings/{id}/audio` generates pre-signed S3 URL (not direct download) ✅
- ✅ `VpBotService.TriggerBotAsync`: calls ECS `RunTask` with all required container overrides (`FIRM_API_URL`, `MEETING_ID`, `MEETING_URL`, `BOT_DISPLAY_NAME`, `BOT_CALLBACK_SECRET`), stores returned task ARN ✅
- ✅ `MeetingService.GetMeetingAsync` enforces `created_by == userId` ✅
- ✅ `Meetings.razor` has `[Authorize]` ✅
- ✅ `MeetingDetail.razor` has `[Authorize]` ✅
- ✅ Status polling every 10s for non-terminal meetings ✅
- ✅ `MeetingDetail.razor` has two tabs (Summary, Transcript) ✅
- ✅ `Dockerfile.debian` uses `debian:bookworm-slim` (not MCR) ✅
- ✅ Uses `dotnet-install.sh` (not `apt-get install dotnet`) ✅
- ✅ Runtime stage has `ENV ASPNETCORE_URLS=http://+:8080` ✅
- ✅ No MCR image references anywhere ✅
- ✅ Bot `meeting-bot.ts` pre-recording writability check present (`.writetest` write+unlink, catch → `recording_failed`) ✅
- ✅ Bot FFmpeg fast-exit detection (exit code != 0 within 5s → `recording_failed`) ✅
- ✅ Bot Dockerfile has `HEALTHCHECK` testing recordings dir writability ✅
- ✅ Bot posts to `FIRM_API_URL + /api/vp/callback` with `X-Bot-Secret: BOT_CALLBACK_SECRET` ✅

---

## Acceptance Criteria Verification

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `SetApplicationName("FortressAI")` exact | ✅ |
| 2 | Auth config keys match FAIT | ✅ |
| 3 | `UseStubAuth` defaults `false` | ✅ |
| 4 | DataProtection persisted to DB | ✅ |
| 5 | OIDC events all present | ✅ |
| 6 | Cookie login path is `/` | ✅ |
| 7 | Auth routes correct | ✅ |
| 8 | Login.razor redirects if authed | ✅ |
| 9 | All 5 entity classes + table attrs | ✅ |
| 10 | Status stored as string | ✅ |
| 11 | DataProtectionKeys DbSet | ✅ |
| 12 | Foreign keys wired correctly | ✅ |
| 13 | CreateTablesAsync non-fatal pattern | ✅ |
| 14 | All tables in extraTables | ✅ |
| 15 | MySqlConnectionStringBuilder | ✅ |
| 16 | POST /api/meetings/join correct | ✅ |
| 17 | POST /api/vp/callback correct | ✅ |
| 18 | Download endpoints auth+ownership | ✅ |
| 19 | Audio endpoint pre-signed S3 | ✅ |
| 20 | VpBotService ECS RunTask correct | ✅ |
| 21 | MeetingService ownership check | ✅ |
| 22 | Meetings.razor [Authorize] | ✅ |
| 23 | MeetingDetail.razor [Authorize] | ✅ |
| 24 | 10s polling for active meetings | ✅ |
| 25 | MeetingDetail two tabs | ✅ |
| 26 | Login.razor no [Authorize] | ✅ |
| 27 | Dockerfile.debian bookworm-slim | ✅ |
| 28 | dotnet-install.sh (not apt) | ✅ |
| 29 | ASPNETCORE_URLS env set | ✅ |
| 30 | No MCR references | ✅ |
| 31 | Pre-recording writability check | ✅ |
| 32 | FFmpeg fast-exit detection | ✅ |
| 33 | Bot Dockerfile HEALTHCHECK | ✅ |
| 34 | Bot posts with X-Bot-Secret | ✅ |
| 35 | **Cookie domain for subdomain sharing** | ❌ Missing `.fortressam.ai` |
| 36 | firm_users auto-provisioning | ⚠️ In UI only, not in API controller |
| 37 | /api/vp/callback secret enforced | ⚠️ Bypassed when secret not configured |
| 38 | Transcript duplicate safety | ⚠️ No dedup on retry |

---

## Summary

**Checklist score: 34/38 ✅** | 2 important issues | 2 minor concerns

The sprint is 90% of the way there. The core auth wiring — DataProtection keys in DB, `SetApplicationName("FortressAI")`, OIDC config — is all correct. The one thing that will silently break SSO is the missing cookie domain. Everything else works but has operational risk under edge cases.

**Required before prod:**
1. Set `Cookie.Domain = ".fortressam.ai"` (or config-driven equivalent) — I1
2. Address the callback secret bypass when unconfigured — I2

**Route to PASS after:** Fix I1 + I2, re-review is not required (both are 1-line changes, no architectural impact).

---

_Reviewed by Hawkeye — 2026-03-08_
