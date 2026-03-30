# Build Report — ADO#1352

**[Tony Stark — BUILD cycle 1]**
**Date:** 2026-03-29
**Branch:** main
**Commits:**
- `25e605d` — feat(ADO#1352): created fip_dev schema + user_microsoft_tokens table on fortress-ai-cluster
- `82edf12` — feat(ADO#1352): FIP captures delegated Graph token at OIDC login, stores in fip_dev
- `d5b4f6d` — feat(ADO#1352): FIRM reads Graph token from fip_dev via FipTokenService, removes standalone MS365 auth, fix FirmMeeting.CreatedBy string→Guid (ElementMappingConvention crash fix)

---

## What was built

Full FIP token architecture forklift. FIP now owns the delegated Graph token lifecycle — capturing access+refresh tokens at OIDC login and persisting to `fip_dev.user_microsoft_tokens` keyed by Entra OID. FIRM reads tokens from that table via the new `FipTokenService` (with automatic refresh). All standalone FIRM MS365 OAuth infrastructure removed.

Also fixed the confirmed root cause of the EF ElementMappingConvention crash: `FirmMeeting.CreatedBy` was `string` but `FirmUser.Id` is `Guid` — EF/Pomelo couldn't resolve the FK type mapping during model finalization, crashing every DbContext instantiation. Fixed with `Guid CreatedBy` in the model and `HasColumnType("char(36)")` in `FirmDbContext`.

---

## Files changed

### DB (no code files)
- Created `fip_dev` schema on `fortress-ai-cluster` (utf8mb4_unicode_ci)
- Created `fip_dev.user_microsoft_tokens` table — keyed by `entra_oid VARCHAR(128) PK`

### FIP
- `fip/src/FortressIntelligencePlatform.Web/Data/FipDbContext.cs` — **NEW** — `FipDbContext` + `FipUserMicrosoftToken` entity; maps to `fip_dev.user_microsoft_tokens`
- `fip/src/FortressIntelligencePlatform.Web/Program.cs` — registered `FipDbContext` factory pointing at `fip_dev`; added 3 OIDC scopes (`offline_access`, `Calendars.Read`, `User.Read`); replaced sync `OnTokenValidated` with async handler that upserts to `fip_dev.user_microsoft_tokens` on every login

### FIRM
- `firm/src/FortressIntelligenceRM.Web/Data/FipDbContext.cs` — **NEW** — copy of FIP's `FipDbContext` for read access to `fip_dev`
- `firm/src/FortressIntelligenceRM.Web/Services/FipTokenService.cs` — **NEW** — reads `fip_dev.user_microsoft_tokens` by Entra OID; handles token refresh via Azure AD token endpoint
- `firm/src/FortressIntelligenceRM.Web/Services/CalendarService.cs` — removed `IFirmMicrosoftTokenService` + `FirmDbContext` dependencies; constructor now takes `FipTokenService`; `GetUpcomingCalendarMeetingsAsync` calls `_fipTokenService.GetValidAccessTokenAsync(entraOid)` directly (no FirmUser lookup needed)
- `firm/src/FortressIntelligenceRM.Web/Components/Pages/Meetings.razor` — removed `@inject MicrosoftTokenService`, `@inject IConfiguration _config`; removed MS365 connection status alert block; removed `ConnectMs365()` method; removed `_ms365Connected`, `_ms365Email`, `_ms365Checked` fields; removed `GetConnectionStatusAsync` call from `OnInitializedAsync`
- `firm/src/FortressIntelligenceRM.Web/Program.cs` — added `FipDbContext` factory registration; removed `FirmMicrosoftTokenService` + `MicrosoftTokenService` registrations; added `FipTokenService` scoped; commented out `TeamsGraphService` + `TranscriptPollingService` hosted services; stubbed `/auth/ms-callback` endpoint
- `firm/src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs` — added `HasColumnType("char(36)")` to `FirmMeeting.CreatedBy` property config (FK type mismatch fix)
- `firm/src/FortressIntelligenceRM.Web/Models/FirmMeeting.cs` — `CreatedBy` changed from `string` to `Guid` (root cause fix for ElementMappingConvention crash)
- `firm/src/FortressIntelligenceRM.Web/Services/MeetingService.cs` — all `userId` params already updated to `Guid` (from stashed ADO#1351 partial work, popped and included here)
- **DELETED:** `FirmMicrosoftTokenService.cs`, `IFirmMicrosoftTokenService.cs`, `MicrosoftTokenService.cs`

---

## Parallelization used

No — all tasks sequential due to dependencies (FIP before FIRM, DB before code).

---

## CC sessions run

2 CC sessions:
1. CC Opus for FIP changes (Part 2)
2. CC Opus for FIRM changes (Part 3)

---

## Acceptance criteria verification

- [ ] FIP login stores row in `fip_dev.user_microsoft_tokens` — **requires live login test post-deploy**
- [ ] FIRM `/meetings` loads without red toast — **requires deploy + live test**
- [ ] FIRM calendar section shows upcoming Teams meetings — **requires deploy + live test**
- [ ] No "Connect Microsoft 365" button visible in FIRM — **verified in Meetings.razor — removed**
- [ ] `TranscriptPollingService` + `TeamsGraphService` absent from FIRM startup logs — **verified in Program.cs — commented out**
- [x] FIP build: 0 errors — **verified locally**
- [x] FIRM build: 0 errors — **verified locally**
- [x] `FirmMeeting.CreatedBy` is `Guid` with `HasColumnType("char(36)")` — **verified in model + FirmDbContext**
- [x] Old token service files deleted — **verified**

---

## Known edge cases / things Clint should scrutinize

1. **`FipTokenService` uses `new HttpClient()` directly** — the spec prescribes this pattern. For production hardening, consider injecting `IHttpClientFactory` instead. Not a blocker but worth noting.

2. ~~**`UserMicrosoftToken` entity still in `FirmDbContext`**~~ — **RESOLVED in cycle 2.** DbSet, `OnModelCreating` config block, and `Models/UserMicrosoftToken.cs` all removed (commit `a1c6c2c`).

3. **FIRM ECS task def needs new env vars** — per the spec: `AzureAd:ClientId`, `AzureAd:TenantId`, `AzureAd:ClientSecret` (same as FIP's app registration), and `FIP_DB_NAME=fip_dev`. These must be added to FIRM's ECS task definition before FIRM can refresh tokens. DevOps task needed.

4. **First-login dependency** — FIRM users must log in through FIP to seed their token row before FIRM's calendar will work. Existing sessions without a `fip_dev` row will get an empty calendar (silent graceful degradation, no crash).

5. **`/auth/ms-callback` endpoint is now a stub** — external OAuth redirect configs pointing to it will get a friendly HTML page instead of a 404. Can be fully removed once confirmed no Azure app registration callbacks point to it.

---

## How to test locally

```bash
# Verify builds
cd /home/fredw/projects/fip
dotnet build fip/src/FortressIntelligencePlatform.Web/FortressIntelligencePlatform.Web.csproj 2>&1 | grep "^Build"
dotnet build firm/src/FortressIntelligenceRM.Web/FortressIntelligenceRM.Web.csproj 2>&1 | grep "^Build"

# Verify DB table
mysql -h fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com -u fortress_mysql -p'=RiQOSU5To4aE3F^' --ssl-mode=REQUIRED -e "USE fip_dev; DESCRIBE user_microsoft_tokens;"

# Verify deleted files are gone
ls firm/src/FortressIntelligenceRM.Web/Services/ | grep -E "FirmMicrosoft|IFirmMicrosoft|^MicrosoftToken"
```

Post-deploy:
1. Log into FIP → check `fip_dev.user_microsoft_tokens` for new row
2. Navigate to FIRM `/meetings` → confirm no red toast
3. Confirm "Upcoming Meetings" section shows calendar events
4. Confirm no "Connect Microsoft 365" alert visible
5. Check CloudWatch `firm-web` logs for `[CalendarService] Returning N Teams meetings`

---

## Build Report — Cycle 2

**[Tony Stark — BUILD cycle 2]**
**Date:** 2026-03-29
**Branch:** main
**Commit:** `a1c6c2c` — fix(ADO#1352): cycle 2 review fixes — dbHost null guard, dead fields, unused usings, legacy UserMicrosoftToken DbSet

---

### What was fixed

Addressed all 4 findings from Clint's cycle 1 review (1 required + 3 nitpicks).

---

### Files changed

- `firm/src/FortressIntelligenceRM.Web/Program.cs` — **I1:** `Server = dbHost` → `Server = dbHost ?? "localhost"` in FipDbContext `MySqlConnectionStringBuilder` block. Prevents local dev crash when `FORTRESS_DB_HOST` is unset.
- `firm/src/FortressIntelligenceRM.Web/Components/Pages/Meetings.razor` — **N1:** Removed dead `_calendarPendingMsg` field declaration and the unreachable `else if (!_calendarLoading && !string.IsNullOrEmpty(_calendarPendingMsg))` render block.
- `firm/src/FortressIntelligenceRM.Web/Services/CalendarService.cs` — **N2:** Removed 2 unused `using` directives (`Microsoft.EntityFrameworkCore`, `FortressIntelligenceRM.Web.Data`) that survived the refactor.
- `firm/src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs` — **N3:** Removed `DbSet<UserMicrosoftToken> UserMicrosoftTokens` property and `modelBuilder.Entity<UserMicrosoftToken>` configuration block from `OnModelCreating`.
- `firm/src/FortressIntelligenceRM.Web/Models/UserMicrosoftToken.cs` — **N3:** Deleted. No remaining references outside of FirmDbContext (now cleaned). Confirmed: `FipDbContext` and `FipTokenService` use `FipUserMicrosoftToken` — separate class, no dependency on this deleted file.

### Note on N3 model deletion

CC (Sonnet) incorrectly concluded `UserMicrosoftToken.cs` was still needed due to confusion with `FipUserMicrosoftToken`. Manual grep confirmed the only references to bare `UserMicrosoftToken` were: (1) the model file's own class declaration, and (2) the `FirmDbContext.cs` entries we removed. File deleted; build verified clean.

---

### Build result

```
Build succeeded.
    11 Warning(s)
    0 Error(s)
```

All 11 warnings are pre-existing (from `TeamsGraphService.cs` and `Meetings.razor` `_joining` field). Zero warnings introduced by cycle 2 changes.
