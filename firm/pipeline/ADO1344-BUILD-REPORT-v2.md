# Build Report — ADO#1344: FIRM Standalone Microsoft Token Management

**Build cycle:** 1  
**Builder:** Tony Stark  
**Commit:** `33d1104`  
**Build result:** ✅ SUCCEEDED — 0 errors, 0 warnings  
**Date:** 2026-03-29

---

---

# Build Report — ADO#1344 Cycle 2: Review Fixes

**Build cycle:** 2  
**Builder:** Tony Stark  
**Commit:** `0f6d962`  
**Build result:** ✅ SUCCEEDED — 0 errors, 0 warnings  
**Date:** 2026-03-29

## What Was Fixed

All 5 findings from Clint's review report (cycle 1) addressed.

## Files Changed

| File | Change |
|---|---|
| `Services/FirmMicrosoftTokenService.cs` | Fix 1: Added `CreatedAt = DateTime.UtcNow` to `UserMicrosoftToken` initializer in `ExchangeCodeAsync` |
| `Program.cs` | Fix 2: OAuth callback now authenticates caller and verifies Entra OID → FIRM user matches `firmUserId` in state (returns 403 on mismatch); Nitpick 3: replaced `ex.Message` with generic user-safe error message |
| `Data/FirmDbContext.cs` | Nitpick 1: `MicrosoftEmail` → `.HasColumnType("varchar(255)")`; Nitpick 2: `ExpiresAt` → `.HasColumnType("datetime(6)")` |

## Self-Review Checklist

- [x] `CreatedAt = DateTime.UtcNow` present in ExchangeCodeAsync object initializer
- [x] OAuth callback verifies authenticated user's FIRM ID matches state before ExchangeCodeAsync
- [x] Mismatch returns 403 (StatusCode(403)) — not 500, not redirect
- [x] `MicrosoftEmail` has `HasColumnType("varchar(255)")`
- [x] `ExpiresAt` has `HasColumnType("datetime(6)")`
- [x] No raw `ex.Message` in any browser-rendered HTML
- [x] Build: 0 errors, 0 warnings
- [x] No scope creep — only touched 3 files flagged by Clint

## Build Output

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## CC Sessions

1 CC session (Sonnet), sequential. All 5 changes in single run.

## Notes for Clint

- Fix 2 eliminates the redundant `db.Users.FindAsync` that followed state parsing — we now load the user once via `verifyDb` during the OID verification, then assign `var firmUser = currentUser`. One fewer DB round-trip on the happy path.
- The 403 for state mismatch gives no information to an attacker — no body content.
- `ex.Message` was the only `ex.*` in an HTML response. Logger still receives the full exception via `LogError(ex, ...)`.

---

---

## CC Invocation

```bash
cat /tmp/tony-ado1344-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

Brief file: `/tmp/tony-ado1344-brief.md`

---

## Spec Compliance Checklist

| Requirement | Status |
|---|---|
| `IFirmMicrosoftTokenService` interface created | ✅ |
| `FirmMicrosoftTokenService` uses `FirmDbContext` only | ✅ |
| No `FaitSharedDbContext` dependency | ✅ |
| Token key is `firm_users.id` (FIRM GUID, string) | ✅ |
| Config keys: `Firm:GraphClientId/TenantId/GraphClientSecret` | ✅ |
| Log prefix: `[FirmMicrosoftTokenService]` | ✅ |
| Interface methods: `GetValidAccessTokenAsync`, `ExchangeCodeAsync`, `RevokeTokenAsync`, `HasToken` | ✅ |
| `UserMicrosoftToken.UserId`: string (not Guid) | ✅ |
| `FirmDbContext` has `UserMicrosoftTokens` DbSet | ✅ |
| Fluent config: `ToTable`, `HasKey`, `HasColumnName` for all props | ✅ |
| `HasColumnType("char(36)")` for UserId | ✅ |
| `HasColumnType("longtext")` for AccessToken, RefreshToken | ✅ |
| `/auth/ms-callback` OAuth endpoint added | ✅ |
| Callback resolves `firm_users.id` from user lookup | ✅ |
| Callback redirects to `/meetings` on success | ✅ |
| `CalendarService` uses `IFirmMicrosoftTokenService` | ✅ |
| `CalendarService` calls `GetValidAccessTokenAsync(firmUser.Id)` | ✅ |
| `Program.cs` registers `IFirmMicrosoftTokenService, FirmMicrosoftTokenService` | ✅ |
| `FaitSharedDbContext` factory removed from `Program.cs` | ✅ |
| `Data/FaitSharedDbContext.cs` deleted | ✅ |
| No leftover `FaitSharedDbContext` references | ✅ |
| `firm_users.fait_user_id` column untouched | ✅ |
| `GuidFormat=None` remains in FIRM connection string | ✅ |
| Build: 0 errors, 0 warnings | ✅ |

---

## Files Changed

| File | Change |
|---|---|
| `Models/UserMicrosoftToken.cs` | `UserId` type changed `Guid` → `string` |
| `Services/IFirmMicrosoftTokenService.cs` | **NEW** — interface with 6 members |
| `Services/FirmMicrosoftTokenService.cs` | Full rewrite — `FirmDbContext`, `string userId`, implements interface |
| `Data/FirmDbContext.cs` | Added `UserMicrosoftTokens` DbSet + fluent entity config |
| `Services/CalendarService.cs` | Injects `IFirmMicrosoftTokenService`, uses `firmUser.Id` directly |
| `Program.cs` | Removed `FaitSharedDbContext`, registered interface, added `/auth/ms-callback` |
| `Data/FaitSharedDbContext.cs` | **DELETED** |

---

## Build Output

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.84
```

---

## Notes for Clint (Code Review)

1. **`UserMicrosoftToken.UserId` is now `string`** — matches the `CHAR(36)` MySQL column with `GuidFormat=None`. FAIT's model uses `Guid`; FIRM's is intentionally different because the MySQL driver won't auto-convert CHAR(36) to Guid with `GuidFormat=None`.

2. **`HasToken` is synchronous** — uses `_dbFactory.CreateDbContext()` (non-async). This is intentional for use in UI bindings. If Clint prefers async, flag it and we'll add `HasTokenAsync`.

3. **`/auth/ms-callback` state format** — `"firmUserId:random"`. The Meetings page will need to initiate consent by calling `tokenService.GetAuthorizationUrl(redirectUri, $"{firmUser.Id}:{Guid.NewGuid()}")` — that UI wiring is out of scope for this WI but documented here for the next WI.

4. **`Firm:MsCallbackUrl` config key** — the callback uses `config["Firm:MsCallbackUrl"]` with fallback to the current host. This env var needs to be set in ECS to the correct redirect URI matching the Azure app registration (same value as FAIT's `MicrosoftGraph:RedirectUri` but pointing at firm's hostname).

---

## How to Test Locally

1. `cd /home/fredw/projects/fip && dotnet build firm/src/FortressIntelligenceRM.Web/FortressIntelligenceRM.Web.csproj`
2. Confirm 0 errors, 0 warnings
3. At runtime: navigate to Meetings page — no token lookup errors in logs
4. Initiate consent → `/auth/ms-callback` → token written to `firm_dev.user_microsoft_tokens` under `firm_users.id`
5. Confirm CloudWatch logs reference `9bdd8169-...` (FIRM user ID), not `b25e0de9-...` (FAIT user ID)
